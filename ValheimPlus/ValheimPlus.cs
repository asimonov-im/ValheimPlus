﻿using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using ValheimPlus.Configurations;
using ValheimPlus.GameClasses;
using ValheimPlus.RPC;
using ValheimPlus.UI;

namespace ValheimPlus
{
    // COPYRIGHT 2021 KEVIN "nx#8830" J. // http://n-x.xyz
    // GITHUB REPOSITORY https://github.com/valheimPlus/ValheimPlus

    [BepInPlugin("org.bepinex.plugins.valheim_plus", "Valheim Plus", numericVersion)]
    public class ValheimPlusPlugin : BaseUnityPlugin
    {
        // Version used when numeric is required (assembly info, bepinex, System.Version parsing).
        public const string numericVersion = "0.9.13.1";

        // Extra version, like alpha/beta/rc/stable. Can leave blank if a stable release.
        public const string versionExtra = "-rc01";

        // Version used when numeric is NOT required (Logging, config file lookup)
        public const string fullVersion = numericVersion + versionExtra;

        // Minimum required version for full compatibility.
        public const string minRequiredNumericVersion = numericVersion;

        // The lowest game version this version of V+ is known to work with.
        public static readonly GameVersion minSupportedGameVersion = new GameVersion(0, 218, 11);

        // The game version this version of V+ was compiled against.
        public static readonly GameVersion targetGameVersion = new GameVersion(0, 218, 15);

        public static string newestVersion = "";
        public static bool isUpToDate = false;
        public static new ManualLogSource Logger { get; private set; }

        public static System.Timers.Timer mapSyncSaveTimer =
            new System.Timers.Timer(TimeSpan.FromMinutes(5).TotalMilliseconds);

        public static readonly string VPlusDataDirectoryPath =
            Paths.BepInExRootPath + Path.DirectorySeparatorChar + "vplus-data";

        private static Harmony harmony = new Harmony("mod.valheim_plus");

        // Project Repository Info
        public static string Repository = "https://github.com/asimonov-im/ValheimPlus/releases/latest";
        public static string ApiRepository = "https://api.github.com/repos/asimonov-im/valheimPlus/releases/latest";

        // Website INI for auto update
        public static string iniFile = "https://github.com/asimonov-im/ValheimPlus/releases/download/" + fullVersion + "/valheim_plus.cfg";

        // mod fails to load when this type is correctly specified as VersionCheck, so we'll just cast it as needed instead.
        private static object versionCheck = new VersionCheck("org.bepinex.plugins.valheim_plus")
        {
            DisplayName = "Valheim Plus",
            CurrentVersion = numericVersion,
            MinimumRequiredVersion = minRequiredNumericVersion,
        };

        // Awake is called once when both the game and the plug-in are loaded
        void Awake()
        {
            Logger = base.Logger;
            Logger.LogInfo($"Valheim Plus full version: {fullVersion}");
            Logger.LogInfo($"Valheim Plus dll file location: '{GetType().Assembly.Location}'");
            Logger.LogInfo("Trying to load the configuration file");

            if (ConfigurationExtra.LoadSettings() != true)
            {
                Logger.LogError("Error while loading configuration file.");
            }
            else
            {

                Logger.LogInfo("Configuration file loaded succesfully.");


                PatchAll();

                isUpToDate = !IsNewVersionAvailable();
                if (!isUpToDate)
                {
                    Logger.LogWarning($"There is a newer version available of ValheimPlus. Please visit {Repository}.");
                }
                else
                {
                    Logger.LogInfo($"ValheimPlus [{fullVersion}] is up to date.");
                }

                //Create VPlus dir if it does not exist.
                if (!Directory.Exists(VPlusDataDirectoryPath)) Directory.CreateDirectory(VPlusDataDirectoryPath);

                //Logo
                //if (Configuration.Current.ValheimPlus.IsEnabled && Configuration.Current.ValheimPlus.mainMenuLogo)
                // No need to exclude with IF, this only loads the images, causes issues if this config setting is changed
                VPlusMainMenu.Load();

                VPlusSettings.Load();

                //Map Sync Save Timer
                if (ZNet.m_isServer && Configuration.Current.Map.IsEnabled && Configuration.Current.Map.shareMapProgression)
                {
                    mapSyncSaveTimer.AutoReset = true;
                    mapSyncSaveTimer.Elapsed += (sender, args) => VPlusMapSync.SaveMapDataToDisk();
                }

                Logger.LogInfo($"ValheimPlus done loading.");
            }
        }

        public static string getCurrentWebIniFile()
        {
            WebClient client = new WebClient();
            client.Headers.Add("User-Agent: V+ Server");
            try
            {
                Logger.LogInfo($"Downloading config from: '{iniFile}'");
                return client.DownloadString(iniFile);
            }
            catch (Exception e)
            {
                Logger.LogError($"Error downloading config from '{iniFile}': {e}");
                return null;
            }
        }

        public static bool isGameVersionTooOld()
        {
            return Version.CurrentVersion < minSupportedGameVersion;
        }

        public static bool isGameVersionNewerThanTarget()
        {
            return Version.CurrentVersion > targetGameVersion;
        }

        public static bool IsNewVersionAvailable()
        {
            WebClient client = new WebClient();

            client.Headers.Add("User-Agent: V+ Server");

            try
            {
                var reply = client.DownloadString(ApiRepository);
                // newest version is the "latest" release in github
                newestVersion = new Regex("\"tag_name\":\"([^\"]*)?\"").Match(reply).Groups[1].Value;
            }
            catch
            {
                Logger.LogWarning("The newest version could not be determined.");
                newestVersion = "Unknown";
            }

            //Parse versions for proper version check
            if (System.Version.TryParse(newestVersion, out var newVersion))
            {
                if (System.Version.TryParse(numericVersion, out var currentVersion))
                {
                    if (currentVersion < newVersion)
                    {
                        return true;
                    }
                }
                else
                {
                    Logger.LogWarning("Couldn't parse current version");
                }
            }
            else //Fallback version check if the version parsing fails
            {
                Logger.LogWarning("Couldn't parse newest version, comparing version strings with equality.");
                if (newestVersion != numericVersion)
                {
                    return true;
                }
            }

            return false;
        }

        public static void PatchAll()
        {
            Logger.LogInfo("Applying patches.");
            try
            {
                // handles annotations
                harmony.PatchAll();

                // manual patches
                // patches that only should run in certain conditions, that otherwise would just cause errors.

                // HarmonyPriority wasn't loading in the order I wanted, so manually load this one after the annotations are all loaded
                harmony.Patch(
                        original: typeof(ZPlayFabMatchmaking).GetMethod("CreateLobby", BindingFlags.NonPublic | BindingFlags.Instance),
                        transpiler: new HarmonyMethod(typeof(ZPlayFabMatchmaking_CreateLobby_Transpiler).GetMethod("Transpiler")));

                // steam only patches
                if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.FullName.Contains("assembly_steamworks")))
                {
                    harmony.Patch(
                        original: AccessTools.TypeByName("SteamGameServer").GetMethod("SetMaxPlayerCount"),
                        prefix: new HarmonyMethod(typeof(ChangeSteamServerVariables).GetMethod("Prefix")));
                }

                // enable mod enforcement with VersionCheck from ServerSync
                ((VersionCheck)versionCheck).ModRequired = Configuration.Current.Server.enforceMod;
                Logger.LogInfo("Patches successfully applied.");
            }
            catch (Exception e)
            {
                Logger.LogError($"Failed to apply patches.");
                if (isGameVersionTooOld())
                {
                    Logger.LogWarning($"This version of Valheim Plus ({fullVersion}) expects a minimum game version of \"{minSupportedGameVersion}\", but this game version is older at \"{Version.CurrentVersion}\". " +
                        $"Please either update the Valheim game, or use an older version of Valheim Plus as per https://github.com/Grantapher/ValheimPlus/blob/grantapher-development/COMPATIBILITY.md.");
                }
                else if (isGameVersionNewerThanTarget())
                {
                    Logger.LogWarning($"This version of Valheim Plus ({fullVersion}) was compiled with a game version of \"{targetGameVersion}\", but this game version is newer at \"{Version.CurrentVersion}\". " +
                        "If you are using the PTB, you likely need to use the non-beta version of the game. " +
                        "Otherwise, the errors seen above likely will require the Valheim Plus mod to be updated. If a game update just came out for Valheim, this may take some time for the mod to be updated. " +
                        "See https://github.com/Grantapher/ValheimPlus/blob/grantapher-development/COMPATIBILITY.md for what game versions are compatible with what mod versions.");
                }
                else
                {
                    Logger.LogWarning($"Valheim Plus failed to apply patches. Please ensure the game version ({Version.GetVersionString()}) is compatible with " +
                        $"the Valheim Plus version ({fullVersion}) at https://github.com/Grantapher/ValheimPlus/blob/grantapher-development/COMPATIBILITY.md. " +
                        $"If it already is, please report a bug at https://github.com/Grantapher/ValheimPlus/issues.");
                }

                // rethrow, otherwise it may not be obvious to the user that patching failed
                throw e;
            }
        }

        public static void UnpatchSelf()
        {
            Logger.LogInfo("Unpatching.");
            try
            {
                harmony.UnpatchSelf();
                Logger.LogInfo("Successfully unpatched.");
            }
            catch (Exception e)
            {
                Logger.LogError($"Failed to unpatch. Exception: {e}");
            }
        }
    }
}
