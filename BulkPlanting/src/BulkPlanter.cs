using System.Collections.Generic;
using UnityEngine;

namespace BulkPlanting
{
    internal enum CellStatus
    {
        Ok,
        Occupied, // something planted already sits on this spot; the preview is hidden
        NoGround,
        NeedCultivated,
        WrongBiome,
        NeedDirt,
        NoBuildZone,
        PrivateZone,
        NoSpace,
        Crowding,
        NoSun,
        TooHot,
        TooCold,
        CantAfford,
    }

    internal struct Cell
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public CellStatus Status;
    }

    internal struct NearbyCrop
    {
        public Vector3 Position;
        public float GrowRadius; // 0 for grown crops, which no longer check for space
    }

    internal sealed class Footprint
    {
        public float GrowRadius;
        public float Extent; // horizontal reach of the sapling's or grown crop's colliders
        public float Spacing;
    }

    /// <summary>What the status panel shows.</summary>
    internal struct LayoutSummary
    {
        public bool Holding; // cultivator + seed selected
        public bool Active;  // bulk mode on and a layout exists
        public string PlantName;
        public string SeedName;
        public int SeedCount;
        public bool FreeBuild;
        public int Rows;
        public int Columns;
        public GridPattern Pattern;
        public float Spacing;
        public bool Snapped;
        public int Plantable;
        public int Blocked;
        public CellStatus FirstProblem;
    }

    internal static class BulkPlanter
    {
        private const float Sqrt3Over2 = 0.8660254f;
        private const int MaxEffectsPerPlacement = 6;

        private static int s_spaceMask;
        private static int s_roofMask;
        private static int s_terrainMask;

        private static readonly Collider[] s_hits = new Collider[256];
        private static readonly Collider[] s_areaHits = new Collider[4096];
        private static readonly List<NearbyCrop> s_nearby = new List<NearbyCrop>();
        private static readonly HashSet<GameObject> s_seen = new HashSet<GameObject>();
        private static readonly Dictionary<string, Footprint> s_footprints = new Dictionary<string, Footprint>();
        private static readonly List<Cell> s_cells = new List<Cell>();
        private static readonly List<GameObject> s_previews = new List<GameObject>();
        private static readonly List<bool> s_previewInvalid = new List<bool>();
        private static readonly EffectList s_noEffects = new EffectList();

        private static GameObject s_previewSource;
        private static GameObject s_inactiveRoot;

        public static LayoutSummary Summary;

        private static int SpaceMask => s_spaceMask != 0 ? s_spaceMask
            : s_spaceMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");
        private static int RoofMask => s_roofMask != 0 ? s_roofMask
            : s_roofMask = LayerMask.GetMask("Default", "static_solid", "piece");
        private static int TerrainMask => s_terrainMask != 0 ? s_terrainMask
            : s_terrainMask = LayerMask.GetMask("terrain");

        // ------------------------------------------------------------------ state

        public static bool IsHoldingPlantable(Player player, out Piece piece, out Plant plant)
        {
            piece = null;
            plant = null;
            if (player == null || player != Player.m_localPlayer || !player.InPlaceMode() || player.IsDead() || player.m_buildPieces == null)
            {
                return false;
            }
            piece = player.m_buildPieces.GetSelectedPiece();
            if (piece == null || piece.m_repairPiece || piece.m_removePiece)
            {
                return false;
            }
            plant = piece.GetComponent<Plant>();
            // Vines attach to walls and are better placed one at a time.
            return plant != null && plant.m_attachDistance <= 0f;
        }

        public static bool IsActive(Player player, out Piece piece, out Plant plant)
        {
            return IsHoldingPlantable(player, out piece, out plant) && Plugin.BulkModeEnabled.Value;
        }

        public static void InvalidateLayout()
        {
            s_cells.Clear();
        }

        public static void ClearFootprintCache()
        {
            s_footprints.Clear();
        }

        // ------------------------------------------------------------------ layout (runs every frame from UpdatePlacementGhost)

        public static void UpdateLayout(Player player)
        {
            if (player != Player.m_localPlayer)
            {
                return;
            }

            bool holding = IsHoldingPlantable(player, out Piece piece, out Plant plant);
            Summary = new LayoutSummary { Holding = holding };

            GameObject ghost = player.m_placementGhost;
            if (!holding || !Plugin.BulkModeEnabled.Value || ghost == null || !ghost.activeSelf
                || player.m_placementStatus == Player.PlacementStatus.NoRayHits)
            {
                s_cells.Clear();
                HidePreviews();
                return;
            }

            if (ghost != s_previewSource)
            {
                DestroyPreviews();
                s_previewSource = ghost;
            }

            Footprint footprint = GetFootprint(piece, plant);
            Vector3 cursor = ghost.transform.position;
            Quaternion rotation = ghost.transform.rotation;
            float yOffset = TryGetGroundHeight(cursor, cursor.y, out float cursorGround)
                ? Mathf.Clamp(cursor.y - cursorGround, -1f, 1f)
                : 0f;

            BuildCells(player, cursor, rotation, yOffset, footprint, piece, plant, out bool snapped, out float spacing);
            ApplyAffordability(player, piece);

            // The vanilla ghost becomes the cursor cell; extra previews show the rest.
            ghost.transform.position = s_cells[0].Position;
            SetHighlight(ghost, s_cells[0].Status != CellStatus.Ok);
            SyncPreviews(ghost);

            FillSummary(player, piece, snapped, spacing);
        }

        private static void BuildCells(Player player, Vector3 cursor, Quaternion rotation, float yOffset, Footprint footprint,
            Piece piece, Plant plant, out bool snapped, out float spacing)
        {
            s_cells.Clear();
            bool hex = Plugin.Pattern.Value == GridPattern.Hex;
            spacing = footprint.Spacing;
            float yaw = rotation.eulerAngles.y;
            Vector3 origin = cursor;
            snapped = false;

            // Continue an existing field: anchor the lattice on the nearest crop and take
            // the field's orientation (and spacing, if it's a bit roomier) from its neighbour.
            if (Plugin.SnapToExistingPlants.Value && TryFindCrop(cursor, spacing * 2f, null, out GameObject anchor))
            {
                origin = anchor.transform.position;
                snapped = true;
                if (TryFindCrop(origin, spacing * 1.75f, anchor, out GameObject neighbour))
                {
                    Vector3 d = neighbour.transform.position - origin;
                    d.y = 0f;
                    float dist = d.magnitude;
                    if (dist > 0.05f && dist <= spacing * 1.6f)
                    {
                        // Rows run along the neighbour direction.
                        yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg - 90f;
                        if (dist >= spacing)
                        {
                            spacing = dist;
                        }
                    }
                }
            }

            // The lattice is symmetric under 60° (hex) / 90° (square) turns, so pick the
            // equivalent orientation whose rows march away from the player.
            Vector3 away = cursor - player.transform.position;
            away.y = 0f;
            float symmetry = hex ? 60f : 90f;
            float bestYaw = yaw;
            float bestDot = float.MinValue;
            for (int k = 0; k < (hex ? 6 : 4); k++)
            {
                float candidate = yaw + k * symmetry;
                float dot = Vector3.Dot(Quaternion.Euler(0f, candidate, 0f) * Vector3.forward, away);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    bestYaw = candidate;
                }
            }
            Quaternion frame = Quaternion.Euler(0f, bestYaw, 0f);
            Vector3 u = frame * Vector3.right;   // along a row
            Vector3 v = frame * Vector3.forward; // row to row, away from the player
            float rowStep = hex ? spacing * Sqrt3Over2 : spacing;

            int c0 = 0;
            int r0 = 0;
            if (snapped)
            {
                SnapToLattice(cursor - origin, u, v, spacing, rowStep, hex, out c0, out r0);
            }

            int rows = Mathf.Clamp(Plugin.Rows.Value, 1, Plugin.MaxGridSize);
            int columns = Mathf.Clamp(Plugin.Columns.Value, 1, Plugin.MaxGridSize);
            int firstColumn = -(columns - 1) / 2;

            // Cursor cell first, then the rest nearest-first: if seeds or stamina run out,
            // the plants closest to where you aimed are the ones that get planted.
            var positions = new List<Vector3>(rows * columns);
            Vector3 cursorCell = Vector3.zero;
            for (int r = 0; r < rows; r++)
            {
                for (int c = firstColumn; c < firstColumn + columns; c++)
                {
                    int row = r0 + r;
                    float x = spacing * (c0 + c + (hex ? 0.5f * Mod2(row) : 0f));
                    Vector3 p = origin + u * x + v * (rowStep * row);
                    if (r == 0 && c == 0)
                    {
                        cursorCell = p;
                    }
                    else
                    {
                        positions.Add(p);
                    }
                }
            }
            positions.Sort((a, b) => (a - cursorCell).sqrMagnitude.CompareTo((b - cursorCell).sqrMagnitude));
            positions.Insert(0, cursorCell);

            GatherNearbyCrops(positions, cursor.y, footprint);

            foreach (Vector3 p in positions)
            {
                Vector3 pos = p;
                CellStatus status;
                if (TryGetGroundHeight(pos, cursor.y, out float ground))
                {
                    pos.y = ground + yOffset;
                    status = Validate(pos, piece, plant, footprint);
                }
                else
                {
                    pos.y = cursor.y;
                    status = CellStatus.NoGround;
                }
                s_cells.Add(new Cell { Position = pos, Rotation = rotation, Status = status });
            }
        }

        private static void SnapToLattice(Vector3 offset, Vector3 u, Vector3 v, float spacing, float rowStep, bool hex, out int column, out int row)
        {
            float lx = Vector3.Dot(offset, u);
            float ly = Vector3.Dot(offset, v);
            int centerRow = Mathf.RoundToInt(ly / rowStep);
            column = 0;
            row = centerRow;
            float best = float.MaxValue;
            for (int r = centerRow - 1; r <= centerRow + 1; r++)
            {
                float shift = hex ? 0.5f * Mod2(r) : 0f;
                int c = Mathf.RoundToInt(lx / spacing - shift);
                float dx = spacing * (c + shift) - lx;
                float dy = rowStep * r - ly;
                float d = dx * dx + dy * dy;
                if (d < best)
                {
                    best = d;
                    column = c;
                    row = r;
                }
            }
        }

        private static int Mod2(int n) => ((n % 2) + 2) % 2;

        // ------------------------------------------------------------------ validation

        private static CellStatus Validate(Vector3 pos, Piece piece, Plant plant, Footprint footprint)
        {
            Heightmap heightmap = Heightmap.FindHeightmap(pos);
            if (heightmap == null)
            {
                return CellStatus.NoGround;
            }
            if (IsOccupied(pos, footprint.Spacing * 0.5f))
            {
                return CellStatus.Occupied;
            }
            if (piece.m_cultivatedGroundOnly && !heightmap.IsCultivated(pos))
            {
                return CellStatus.NeedCultivated;
            }
            if (piece.m_onlyInBiome != Heightmap.Biome.None && (Heightmap.FindBiome(pos) & piece.m_onlyInBiome) == 0)
            {
                return CellStatus.WrongBiome;
            }
            if (piece.m_vegetationGroundOnly)
            {
                Heightmap.Biome b = heightmap.GetBiome(pos);
                float mask = heightmap.GetVegetationMask(pos);
                if (b == Heightmap.Biome.AshLands ? mask > 0.1f : mask < 0.25f)
                {
                    return CellStatus.NeedDirt;
                }
            }
            if (Location.IsInsideNoBuildLocation(pos))
            {
                return CellStatus.NoBuildZone;
            }
            if (!PrivateArea.CheckAccess(pos, 0f, flash: false))
            {
                return CellStatus.PrivateZone;
            }

            if (!Plugin.RequireGrowableSpot.Value)
            {
                return CellStatus.Ok;
            }

            // Mirrors Plant.UpdateHealth / HaveGrowSpace / HaveRoof.
            Heightmap.Biome biome = heightmap.GetBiome(pos);
            if ((biome & plant.m_biome) == 0)
            {
                return CellStatus.WrongBiome;
            }
            bool shielded = ShieldGenerator.IsInsideShield(pos);
            if (!plant.m_tolerateHeat && biome == Heightmap.Biome.AshLands && !shielded)
            {
                return CellStatus.TooHot;
            }
            if (!plant.m_tolerateCold && (biome == Heightmap.Biome.DeepNorth || biome == Heightmap.Biome.Mountain) && !shielded)
            {
                return CellStatus.TooCold;
            }
            if (Physics.Raycast(pos, Vector3.up, 100f, RoofMask))
            {
                return CellStatus.NoSun;
            }
            if (Physics.OverlapSphereNonAlloc(pos, footprint.GrowRadius, s_hits, SpaceMask) > 0)
            {
                return CellStatus.NoSpace;
            }
            // Don't stunt existing saplings whose grow radius would reach this plant.
            foreach (NearbyCrop other in s_nearby)
            {
                if (other.GrowRadius > 0f && Vector3.Distance(other.Position, pos) < other.GrowRadius + footprint.Extent)
                {
                    return CellStatus.Crowding;
                }
            }
            return CellStatus.Ok;
        }

        private static bool IsOccupied(Vector3 pos, float radius)
        {
            foreach (NearbyCrop other in s_nearby)
            {
                Vector3 d = other.Position - pos;
                if (Mathf.Abs(d.y) < 2f && d.x * d.x + d.z * d.z < radius * radius)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>One physics query for the whole grid instead of several per cell.</summary>
        private static void GatherNearbyCrops(List<Vector3> positions, float y, Footprint footprint)
        {
            s_nearby.Clear();
            s_seen.Clear();

            Vector3 center = Vector3.zero;
            foreach (Vector3 p in positions)
            {
                center += p;
            }
            center /= positions.Count;
            center.y = y;
            float reach = 0f;
            foreach (Vector3 p in positions)
            {
                reach = Mathf.Max(reach, new Vector2(p.x - center.x, p.z - center.z).magnitude);
            }
            // Wide enough to catch tree saplings, whose grow radius can be several meters.
            reach += 5f + footprint.Extent + footprint.Spacing;

            int n = Physics.OverlapSphereNonAlloc(center, reach, s_areaHits, SpaceMask);
            for (int i = 0; i < n; i++)
            {
                Plant plant = s_areaHits[i].GetComponentInParent<Plant>();
                if (plant != null)
                {
                    if (s_seen.Add(plant.gameObject))
                    {
                        s_nearby.Add(new NearbyCrop { Position = plant.transform.position, GrowRadius = plant.m_growRadius });
                    }
                    continue;
                }
                Pickable pickable = s_areaHits[i].GetComponentInParent<Pickable>();
                if (pickable != null && s_seen.Add(pickable.gameObject))
                {
                    s_nearby.Add(new NearbyCrop { Position = pickable.transform.position, GrowRadius = 0f });
                }
            }
        }

        /// <summary>Nearest sapling, or grown crop standing on cultivated soil.</summary>
        private static bool TryFindCrop(Vector3 center, float radius, GameObject exclude, out GameObject crop)
        {
            crop = null;
            float best = float.MaxValue;
            int n = Physics.OverlapSphereNonAlloc(center, radius, s_hits, SpaceMask);
            for (int i = 0; i < n; i++)
            {
                GameObject root = null;
                Plant plant = s_hits[i].GetComponentInParent<Plant>();
                if (plant != null)
                {
                    root = plant.gameObject;
                }
                else
                {
                    Pickable pickable = s_hits[i].GetComponentInParent<Pickable>();
                    if (pickable != null)
                    {
                        Heightmap hm = Heightmap.FindHeightmap(pickable.transform.position);
                        if (hm != null && hm.IsCultivated(pickable.transform.position))
                        {
                            root = pickable.gameObject;
                        }
                    }
                }
                if (root == null || root == exclude)
                {
                    continue;
                }
                Vector3 d = root.transform.position - center;
                d.y = 0f;
                float dist = d.sqrMagnitude;
                if (dist < best && (exclude == null || dist > 0.0025f))
                {
                    best = dist;
                    crop = root;
                }
            }
            return crop != null;
        }

        private static bool TryGetGroundHeight(Vector3 pos, float referenceY, out float height)
        {
            var start = new Vector3(pos.x, referenceY + 6f, pos.z);
            if (Physics.Raycast(start, Vector3.down, out RaycastHit hit, 12f, TerrainMask))
            {
                height = hit.point.y;
                return true;
            }
            height = 0f;
            return false;
        }

        // ------------------------------------------------------------------ spacing

        /// <summary>
        /// A plant only grows if no other collider (besides unhealthy saplings) sits within its
        /// grow radius (Plant.HaveGrowSpace). So the tightest safe spacing is the grow radius plus
        /// the widest collider of the neighbour, whether it is still a sapling or fully grown.
        /// </summary>
        private static Footprint GetFootprint(Piece piece, Plant plant)
        {
            if (s_footprints.TryGetValue(piece.name, out Footprint cached))
            {
                return cached;
            }

            float extent = ColliderExtent(plant.gameObject) * Mathf.Abs(plant.transform.localScale.x);
            foreach (GameObject grown in plant.m_grownPrefabs)
            {
                if (grown != null)
                {
                    extent = Mathf.Max(extent, ColliderExtent(grown) * Mathf.Max(plant.m_maxScale, plant.m_minScale));
                }
            }

            var footprint = new Footprint
            {
                GrowRadius = plant.m_growRadius,
                Extent = extent,
                Spacing = Mathf.Max(plant.m_growRadius + extent + Plugin.SpacingMargin.Value, 0.3f),
            };
            s_footprints[piece.name] = footprint;
            Plugin.Log.LogInfo($"{piece.name}: grow radius {footprint.GrowRadius:0.00} m, collider reach {extent:0.00} m -> spacing {footprint.Spacing:0.00} m");
            return footprint;
        }

        private static float ColliderExtent(GameObject prefab)
        {
            float best = 0f;
            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
            foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(includeInactive: true))
            {
                if (((1 << collider.gameObject.layer) & SpaceMask) == 0 || !TryGetLocalBounds(collider, out Bounds b))
                {
                    continue;
                }
                Matrix4x4 m = toRoot * collider.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? b.min.x : b.max.x,
                        (i & 2) == 0 ? b.min.y : b.max.y,
                        (i & 4) == 0 ? b.min.z : b.max.z);
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    best = Mathf.Max(best, Mathf.Sqrt(p.x * p.x + p.z * p.z));
                }
            }
            return best;
        }

        private static bool TryGetLocalBounds(Collider collider, out Bounds bounds)
        {
            switch (collider)
            {
                case BoxCollider box:
                    bounds = new Bounds(box.center, box.size);
                    return true;
                case SphereCollider sphere:
                    bounds = new Bounds(sphere.center, Vector3.one * (sphere.radius * 2f));
                    return true;
                case CapsuleCollider capsule:
                    Vector3 size = Vector3.one * (capsule.radius * 2f);
                    size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2f);
                    bounds = new Bounds(capsule.center, size);
                    return true;
                case MeshCollider mesh when mesh.sharedMesh != null:
                    bounds = mesh.sharedMesh.bounds;
                    return true;
                default:
                    bounds = default;
                    return false;
            }
        }

        // ------------------------------------------------------------------ costs

        private static bool IsFree(Player player, Piece piece)
        {
            return player.m_noPlacementCost || ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey());
        }

        /// <summary>How many plants the player can pay for right now (seeds, stamina, durability).</summary>
        private static int CountAffordable(Player player, Piece piece)
        {
            int count = int.MaxValue;
            if (!IsFree(player, piece))
            {
                foreach (Piece.Requirement req in piece.m_resources)
                {
                    if (req.m_resItem != null && req.m_amount > 0)
                    {
                        count = Mathf.Min(count, player.m_inventory.CountItems(req.m_resItem.m_itemData.m_shared.m_name) / req.m_amount);
                    }
                }
            }
            float stamina = player.GetBuildStamina();
            if (Plugin.StaminaPerPlant.Value && stamina > 0f)
            {
                count = Mathf.Min(count, Mathf.Max(1, Mathf.FloorToInt(player.GetStamina() / stamina)));
            }
            ItemDrop.ItemData tool = player.GetRightItem();
            float drain = DurabilityDrain(player, tool);
            if (drain > 0f)
            {
                count = Mathf.Min(count, Mathf.Max(1, Mathf.FloorToInt(tool.m_durability / drain)));
            }
            return count;
        }

        private static float DurabilityDrain(Player player, ItemDrop.ItemData tool)
        {
            if (!Plugin.DurabilityPerPlant.Value || tool == null || !tool.m_shared.m_useDurability)
            {
                return 0f;
            }
            return player.GetPlaceDurability(tool) * Game.m_durabilityRate;
        }

        private static bool HaveResourcesFor(Player player, Piece piece, int plants)
        {
            foreach (Piece.Requirement req in piece.m_resources)
            {
                if (req.m_resItem != null && req.m_amount > 0
                    && player.m_inventory.CountItems(req.m_resItem.m_itemData.m_shared.m_name) < req.m_amount * plants)
                {
                    return false;
                }
            }
            return true;
        }

        private static void ApplyAffordability(Player player, Piece piece)
        {
            int affordable = CountAffordable(player, piece);
            int planned = 0;
            for (int i = 0; i < s_cells.Count; i++)
            {
                Cell cell = s_cells[i];
                if (cell.Status == CellStatus.Ok && ++planned > affordable)
                {
                    cell.Status = CellStatus.CantAfford;
                    s_cells[i] = cell;
                }
            }
        }

        // ------------------------------------------------------------------ placement (replaces Player.TryPlacePiece)

        /// <summary>
        /// Places every valid cell. Returns true if anything was planted, in which case vanilla
        /// UpdatePlacement charges the usual cost for one plant; the extra plants are charged here.
        /// </summary>
        public static bool PlaceAll(Player player, Piece piece)
        {
            player.UpdatePlacementGhost(flashGuardStone: true);
            if (s_cells.Count == 0)
            {
                player.Message(MessageHud.MessageType.Center, "$msg_invalidplacement");
                return false;
            }

            bool free = IsFree(player, piece);
            bool cheated = (player.m_inventory.ItemCheated(piece.m_resources) || player.NoCostCheat()) && !PlayerProfile.s_bypassCheatChecks;
            ItemDrop.ItemData tool = player.GetRightItem();
            float staminaCost = player.GetBuildStamina();
            float drain = DurabilityDrain(player, tool);
            Skills.SkillType skill = player.m_buildPieces.m_skill;
            int savedRotation = player.m_placeRotation;
            EffectList placeEffect = piece.m_placeEffect;
            int placed = 0;

            try
            {
                foreach (Cell cell in s_cells)
                {
                    if (cell.Status != CellStatus.Ok)
                    {
                        continue;
                    }
                    bool first = placed == 0;
                    // Extras must leave enough for the one plant vanilla charges afterwards.
                    if (!first)
                    {
                        if (!free && !HaveResourcesFor(player, piece, 2)) break;
                        if (Plugin.StaminaPerPlant.Value && staminaCost > 0f && player.GetStamina() < staminaCost * 2f) break;
                        if (drain > 0f && tool.m_durability < drain * 2f) break;
                    }

                    Quaternion rotation = piece.m_randomInitBuildRotation
                        ? Quaternion.Euler(0f, Random.Range(0, 16) * 22.5f, 0f)
                        : cell.Rotation;

                    // A dozen identical "plop" sounds at once is just noise.
                    piece.m_placeEffect = placed < MaxEffectsPerPlacement ? placeEffect : s_noEffects;

                    Game.instance.IncrementPlayerStat(PlayerStatType.Builds, 1f, cheated);
                    Game.instance.GetPlayerProfile().IncrementStatBuildPiecePlaced(piece.m_name, 1f, cheated);
                    player.PlacePiece(piece, cell.Position, rotation, doAttack: first, cheated);
                    placed++;

                    if (!first)
                    {
                        if (!free) player.ConsumeResources(piece.m_resources, 0);
                        if (Plugin.StaminaPerPlant.Value) player.UseStamina(staminaCost);
                        if (drain > 0f) tool.m_durability -= drain;
                        if (Plugin.SkillPerPlant.Value && skill != Skills.SkillType.None) player.RaiseSkill(skill);
                    }
                }
            }
            finally
            {
                piece.m_placeEffect = placeEffect;
                player.m_placeRotation = savedRotation;
            }

            if (placed == 0)
            {
                player.Message(MessageHud.MessageType.Center, DescribeProblem(FirstProblem()));
                return false;
            }
            return true;
        }

        private static CellStatus FirstProblem()
        {
            foreach (Cell cell in s_cells)
            {
                if (cell.Status != CellStatus.Ok && cell.Status != CellStatus.Occupied)
                {
                    return cell.Status;
                }
            }
            return s_cells.Count > 0 ? s_cells[0].Status : CellStatus.Ok;
        }

        public static string DescribeProblem(CellStatus status)
        {
            switch (status)
            {
                case CellStatus.Occupied: return "Already planted here";
                case CellStatus.NoGround: return "$msg_invalidplacement";
                case CellStatus.NeedCultivated: return "$msg_needcultivated";
                case CellStatus.WrongBiome: return "$msg_wrongbiome";
                case CellStatus.NeedDirt: return "$msg_needdirt";
                case CellStatus.NoBuildZone: return "$msg_nobuildzone";
                case CellStatus.PrivateZone: return "$msg_privatezone";
                case CellStatus.NoSpace: return "$msg_needspace";
                case CellStatus.Crowding: return "Too close to other plants";
                case CellStatus.NoSun: return "Plants need open sky";
                case CellStatus.TooHot: return "$piece_plant_toohot";
                case CellStatus.TooCold: return "$piece_plant_toocold";
                case CellStatus.CantAfford: return "$msg_missingrequirement";
                default: return "";
            }
        }

        // ------------------------------------------------------------------ previews

        private static void SyncPreviews(GameObject ghost)
        {
            int needed = s_cells.Count - 1;
            for (int i = 0; i < needed; i++)
            {
                if (i >= s_previews.Count)
                {
                    s_previews.Add(null);
                    s_previewInvalid.Add(false);
                }
                if (s_previews[i] == null)
                {
                    s_previews[i] = CreatePreview(ghost);
                    s_previewInvalid[i] = false;
                }

                Cell cell = s_cells[i + 1];
                GameObject preview = s_previews[i];
                bool visible = cell.Status != CellStatus.Occupied;
                if (preview.activeSelf != visible)
                {
                    preview.SetActive(visible);
                }
                if (!visible)
                {
                    continue;
                }
                preview.transform.SetPositionAndRotation(cell.Position, cell.Rotation);
                bool invalid = cell.Status != CellStatus.Ok;
                if (invalid != s_previewInvalid[i])
                {
                    SetHighlight(preview, invalid);
                    s_previewInvalid[i] = invalid;
                }
            }
            for (int i = needed; i < s_previews.Count; i++)
            {
                if (s_previews[i] != null && s_previews[i].activeSelf)
                {
                    s_previews[i].SetActive(false);
                }
            }
        }

        /// <summary>
        /// Clones the placement ghost as pure visuals. The clone is created under an inactive
        /// parent so no Awake runs, then every script and collider is stripped before it is
        /// activated (a live Plant without a ZNetView would throw in the SlowUpdate loop).
        /// </summary>
        private static GameObject CreatePreview(GameObject ghost)
        {
            if (s_inactiveRoot == null)
            {
                s_inactiveRoot = new GameObject("BulkPlanting_PreviewStaging");
                s_inactiveRoot.SetActive(false);
            }

            GameObject clone = Object.Instantiate(ghost, s_inactiveRoot.transform);
            clone.name = ghost.name + " (bulk preview)";
            // A few passes, since [RequireComponent] can make removal order matter.
            for (int pass = 0; pass < 3; pass++)
            {
                MonoBehaviour[] scripts = clone.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
                if (scripts.Length == 0)
                {
                    break;
                }
                for (int i = scripts.Length - 1; i >= 0; i--)
                {
                    Object.DestroyImmediate(scripts[i]);
                }
            }
            foreach (Collider collider in clone.GetComponentsInChildren<Collider>(includeInactive: true))
            {
                Object.DestroyImmediate(collider);
            }
            clone.transform.SetParent(null, worldPositionStays: false);
            clone.SetActive(true);
            return clone;
        }

        private static void SetHighlight(GameObject go, bool invalid)
        {
            if (invalid)
            {
                MaterialMan.instance.SetValue(go, ShaderProps._Color, Color.red);
                MaterialMan.instance.SetValue(go, ShaderProps._EmissionColor, Color.red * 0.7f);
            }
            else
            {
                MaterialMan.instance.ResetValue(go, ShaderProps._Color);
                MaterialMan.instance.ResetValue(go, ShaderProps._EmissionColor);
            }
        }

        public static void HidePreviews()
        {
            foreach (GameObject preview in s_previews)
            {
                if (preview != null && preview.activeSelf)
                {
                    preview.SetActive(false);
                }
            }
        }

        public static void DestroyPreviews()
        {
            foreach (GameObject preview in s_previews)
            {
                if (preview != null)
                {
                    Object.Destroy(preview);
                }
            }
            s_previews.Clear();
            s_previewInvalid.Clear();
            s_previewSource = null;
        }

        // ------------------------------------------------------------------ summary for the status panel

        private static void FillSummary(Player player, Piece piece, bool snapped, float spacing)
        {
            Summary.Active = true;
            Summary.PlantName = Localization.instance.Localize(piece.m_name);
            Summary.FreeBuild = IsFree(player, piece);
            Summary.Rows = Plugin.Rows.Value;
            Summary.Columns = Plugin.Columns.Value;
            Summary.Pattern = Plugin.Pattern.Value;
            Summary.Spacing = spacing;
            Summary.Snapped = snapped;

            foreach (Piece.Requirement req in piece.m_resources)
            {
                if (req.m_resItem != null && req.m_amount > 0)
                {
                    Summary.SeedName = Localization.instance.Localize(req.m_resItem.m_itemData.m_shared.m_name);
                    Summary.SeedCount = player.m_inventory.CountItems(req.m_resItem.m_itemData.m_shared.m_name);
                    break;
                }
            }

            foreach (Cell cell in s_cells)
            {
                if (cell.Status == CellStatus.Ok) Summary.Plantable++;
                else if (cell.Status != CellStatus.Occupied) Summary.Blocked++;
            }
            Summary.FirstProblem = FirstProblem();
        }
    }
}
