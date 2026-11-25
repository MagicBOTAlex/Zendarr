{
  description = "Zendarr — one unified backend and one UI for movies, TV and anime";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
  };

  outputs =
    { self, nixpkgs }:
    let
      systems = [
        "x86_64-linux"
        "aarch64-linux"
        "aarch64-darwin"
      ];

      forAllSystems =
        f: nixpkgs.lib.genAttrs systems (system: f system (import nixpkgs { inherit system; }));

      # Everything is derived from one package set so the shell, packages and
      # apps stay consistent.
      mkTools =
        pkgs:
        let
          lib = pkgs.lib;

          # The unified backend targets net10.0 (src/Zendarr.Series pins SDK
          # 10.0.401 via global.json). The retired Movies tree is no longer
          # built, so only the .NET 10 SDK is needed.
          dotnet = pkgs.writeShellScriptBin "dotnet" ''
            root="${pkgs.dotnet-sdk_10}"
            export DOTNET_ROOT="$root/share/dotnet"
            export DOTNET_HOST_PATH="$root/share/dotnet/dotnet"
            export DOTNET_CLI_TELEMETRY_OPTOUT=1
            export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
            exec "$root/bin/dotnet" "$@"
          '';

          toolchain = [
            dotnet
            pkgs.nodejs_24
            pkgs.yarn
            pkgs.python3
            pkgs.curl
            pkgs.git
            pkgs.jq
            pkgs.pkg-config
            pkgs.coreutils
            pkgs.util-linux
            pkgs.cacert
            # Media compression: ffmpeg (NVENC/VA-API/software) resolved from PATH;
            # libva-utils supplies `vainfo` for VA-API diagnosis. On NixOS the host
            # still provides the GPU driver/render nodes (`/dev/dri`).
            pkgs.ffmpeg
            pkgs.libva-utils
          ];

          # Wrapper that runs a repo script against the user's checkout rather
          # than the read-only store copy. `args` are fixed arguments appended
          # to the script invocation.
          mkApp =
            name: script: args:
            pkgs.writeShellApplication {
              inherit name;
              runtimeInputs = toolchain;
              text = ''
                repo="$(git rev-parse --show-toplevel 2>/dev/null || echo "$PWD")"
                exec "$repo/${script}" ${args}
              '';
            };

          # `nix run` attaches to the service logs in the foreground; Ctrl+C
          # stops everything.
          run = mkApp "zendarr" "zendarr.sh" "--attach";
          build = mkApp "zendarr-build" "build.sh" "";
          test = mkApp "zendarr-test" "test.sh" "";
          stop = mkApp "zendarr-stop" "zendarr-stop.sh" "";

          # Compiles the unified backend and the unified SPA and installs the
          # output tree. Needs network for NuGet + yarn, so it builds with the
          # sandbox disabled (see nix.conf).
          zendarr = pkgs.stdenv.mkDerivation (finalAttrs: {
            pname = "zendarr";
            version = "0.1.0";
            src = ./.;

            nativeBuildInputs = toolchain;

            env = {
              DOTNET_CLI_TELEMETRY_OPTOUT = "1";
              DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1";
              NODE_OPTIONS = "--max-old-space-size=8192";
              SSL_CERT_FILE = "${pkgs.cacert}/etc/ssl/certs/ca-bundle.crt";
              NODE_EXTRA_CA_CERTS = "${pkgs.cacert}/etc/ssl/certs/ca-bundle.crt";
              GIT_SSL_CAINFO = "${pkgs.cacert}/etc/ssl/certs/ca-bundle.crt";
              CURL_CA_BUNDLE = "${pkgs.cacert}/etc/ssl/certs/ca-bundle.crt";
            };

            buildPhase = ''
              runHook preBuild
              export HOME="$TMPDIR"
              export YARN_CACHE_FOLDER="$TMPDIR/yarn-cache"
              export NUGET_PACKAGES="$TMPDIR/nuget"
              bash build.sh
              runHook postBuild
            '';

            installPhase = ''
              runHook preInstall
              mkdir -p "$out"

              # One backend: the unified engine.
              mkdir -p "$out/src/Zendarr.Series"
              cp -a src/Zendarr.Series/_output "$out/src/Zendarr.Series/_output"

              # One UI: the unified SPA.
              mkdir -p "$out/src/Zendarr.Web/_output"
              cp -a src/Zendarr.Web/_output/UI "$out/src/Zendarr.Web/_output/UI"

              # Stage the unified UI into the backend's served UI folder
              # (<StartUpFolder>/UI) so the backend serves it directly at root.
              rm -rf "$out/src/Zendarr.Series/_output/net10.0/UI"
              mkdir -p "$out/src/Zendarr.Series/_output/net10.0/UI"
              cp -a src/Zendarr.Web/_output/UI/. "$out/src/Zendarr.Series/_output/net10.0/UI/"

              cp -a \
                zendarr.sh zendarr-stop.sh build.sh test.sh \
                Logo README.md LICENSE NOTICE "$out/"
              runHook postInstall
            '';

            meta = with lib; {
              description = "One unified backend and one UI for movies, TV and anime";
              license = licenses.gpl3Plus;
              platforms = platforms.unix;
            };
          });
        in
        {
          inherit
            toolchain
            run
            build
            test
            stop
            zendarr
            dotnet
            ;
        };
    in
    {
      devShells = forAllSystems (
        system: pkgs:
        let
          tools = mkTools pkgs;
        in
        {
          default = pkgs.mkShell {
            packages = tools.toolchain;
            shellHook = ''
              export SSL_CERT_FILE="${pkgs.cacert}/etc/ssl/certs/ca-bundle.crt"
              export NODE_EXTRA_CA_CERTS="${pkgs.cacert}/etc/ssl/certs/ca-bundle.crt"
              export GIT_SSL_CAINFO="${pkgs.cacert}/etc/ssl/certs/ca-bundle.crt"
              export CURL_CA_BUNDLE="${pkgs.cacert}/etc/ssl/certs/ca-bundle.crt"
              echo "Zendarr dev shell"
              echo "  dotnet: $(dotnet --version 2>/dev/null || echo '(missing)')"
              echo "  node:   $(node --version 2>/dev/null || echo '(missing)')"
              echo "  yarn:   $(yarn --version 2>/dev/null || echo '(missing)')"
              echo "  python: $(python3 --version 2>/dev/null || echo '(missing)')"
              echo
              echo "  nix run .#build   build the unified backend + UI"
              echo "  nix run           start (foreground, Ctrl+C stops)"
            '';
          };
        }
      );

      packages = forAllSystems (
        system: pkgs:
        let
          tools = mkTools pkgs;
        in
        {
          default = tools.zendarr;
          zendarr = tools.zendarr;
          run = tools.run;
        }
      );

      apps = forAllSystems (
        system: pkgs:
        let
          tools = mkTools pkgs;
        in
        {
          default = {
            type = "app";
            program = "${tools.run}/bin/zendarr";
            meta.description = "Start Zendarr (foreground)";
          };
          build = {
            type = "app";
            program = "${tools.build}/bin/zendarr-build";
            meta.description = "Build the Zendarr backend + UI";
          };
          test = {
            type = "app";
            program = "${tools.test}/bin/zendarr-test";
            meta.description = "Run the Zendarr test suite";
          };
          stop = {
            type = "app";
            program = "${tools.stop}/bin/zendarr-stop";
            meta.description = "Stop Zendarr";
          };
        }
      );

      # NixOS module: `services.zendarr`.
      nixosModules.default =
        {
          config,
          lib,
          pkgs,
          ...
        }:
        let
          tools = mkTools pkgs;
          cfg = config.services.zendarr;
        in
        {
          options.services.zendarr = {
            enable = lib.mkEnableOption "Zendarr";

            package = lib.mkOption {
              type = lib.types.package;
              default = tools.zendarr;
              defaultText = lib.literalExpression "zendarr.packages.\${system}.zendarr";
              description = "The Zendarr package to use.";
            };

            user = lib.mkOption {
              type = lib.types.str;
              default = "zendarr";
              description = "User the service runs as.";
            };

            group = lib.mkOption {
              type = lib.types.str;
              default = "zendarr";
              description = "Group the service runs as.";
            };

            port = lib.mkOption {
              type = lib.types.port;
              default = 6868;
              description = "Port the unified UI + API listener binds to.";
            };

            openFirewall = lib.mkOption {
              type = lib.types.bool;
              default = false;
              description = "Open the listen port in the firewall.";
            };

            disableAuth = lib.mkOption {
              type = lib.types.bool;
              default = false;
              description = "Disable the login page (API-key auth is unaffected).";
            };

            environmentFile = lib.mkOption {
              type = lib.types.nullOr lib.types.path;
              default = null;
              example = "/run/secrets/zendarr.env";
              description = "Optional runtime environment file (for example secrets).";
            };
          };

          config = lib.mkIf cfg.enable {
            users.users.${cfg.user} = {
              isSystemUser = true;
              group = cfg.group;
              home = "/var/lib/zendarr";
            };
            users.groups.${cfg.group} = { };

            systemd.services.zendarr = {
              description = "Zendarr unified Sonarr/Radarr backend";
              wantedBy = [ "multi-user.target" ];
              wants = [ "network-online.target" ];
              after = [ "network-online.target" ];
              # ffmpeg/VA-API for media compression (resolved from PATH).
              path = [
                pkgs.ffmpeg
                pkgs.libva-utils
              ];

              serviceConfig = {
                ExecStart = "${tools.dotnet}/bin/dotnet ${cfg.package}/src/Zendarr.Series/_output/net10.0/Sonarr.dll --data /var/lib/zendarr --nobrowser";
                User = cfg.user;
                Group = cfg.group;
                StateDirectory = "zendarr";
                WorkingDirectory = "/var/lib/zendarr";
                Environment = [
                  "ZENDARR_DATA=/var/lib/zendarr"
                  "ZENDARR_PORT=${toString cfg.port}"
                  "Zendarr__Server__Port=${toString cfg.port}"
                  "Zendarr__Server__UrlBase="
                  "ZENDARR_TRANSCODE_TEMP_FOLDER=/var/lib/zendarr/transcode"
                  "ZENDARR_DISABLE_AUTH=${if cfg.disableAuth then "true" else "false"}"
                ];
                EnvironmentFile = lib.optional (cfg.environmentFile != null) cfg.environmentFile;
                Restart = "on-failure";
                RestartSec = 5;

                NoNewPrivileges = true;
                ProtectSystem = "strict";
                ProtectHome = true;
                PrivateTmp = true;
                ProtectKernelTunables = true;
                ProtectControlGroups = true;
                RestrictAddressFamilies = [
                  "AF_INET"
                  "AF_INET6"
                  "AF_UNIX"
                ];
                ReadWritePaths = [ "/var/lib/zendarr" ];
                CapabilityBoundingSet = "";
                AmbientCapabilities = "";
              };
            };

            networking.firewall.allowedTCPPorts = lib.optional cfg.openFirewall cfg.port;
          };
        };

      formatter = forAllSystems (system: pkgs: pkgs.nixfmt);
    };
}
