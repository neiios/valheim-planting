{
  description = "BulkPlanting - a Valheim mod for planting crops in neat, compact grids";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixpkgs-unstable";

  outputs = { self, nixpkgs }:
    let
      system = "x86_64-linux";
      pkgs = nixpkgs.legacyPackages.${system};
    in {
      devShells.${system}.default = pkgs.mkShell {
        packages = with pkgs; [
          dotnet-sdk_8
          ilspycmd # handy for reading the game's code when Valheim updates
          curl
          unzip
        ];
        DOTNET_CLI_TELEMETRY_OPTOUT = "1";
        DOTNET_NOLOGO = "1";
      };
    };
}
