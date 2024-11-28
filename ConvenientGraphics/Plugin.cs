using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
//using XivCommon;

using ConvenientGraphics.Windows;
using MemoryManager;
using SettingsManager;


namespace ConvenientGraphics
{
    public unsafe class Plugin : IDalamudPlugin
    {
        [PluginService] public static IDalamudPluginInterface? iPluginInterface { get; private set; } = null;
        [PluginService] public static IFramework? iFramework { get; private set; } = null;
        [PluginService] public static ICommandManager? CommandManager { get; private set; } = null;
        [PluginService] public static IClientState? ClientState { get; private set; } = null;
        [PluginService] public static IChatGui? ChatGui { get; private set; } = null;
        [PluginService] public static ICondition? Condition { get; private set; } = null;
        [PluginService] public static IGameGui? GameGui { get; private set; } = null;
        [PluginService] public static IPluginLog? Log { get; private set; } = null;

        public string Name => "ConvenientGraphics";
        private const string CommandName = "/congraph";

        public static SharedMemoryManager smm = new SharedMemoryManager();
        public static ConfigManager cfgManager = new ConfigManager();

        public Configuration cfg { get; init; }
        public WindowSystem WindowSystem = new("ConvenientGraphics");
        private MainWindow MainWindow { get; init; }
        //private XivCommonBase chatHandler { get; set; }
        private Framework* frameworkInstance = Framework.Instance();

        private List<ushort> cityZones = new List<ushort>() { 
            128, 129, // limsa
            130, 131, // uldah
            132, 133, // gridania
            418, 419, // ishgard
            478, // idleshire
            628, // kugane
            635, // ralgarsreach
            819, // crystalium
            820, // eulmore
            963, // radzahan
            962, // sharlayan
            1185, // Tuliyollal
            1186, // solution9
        };



        
        private bool isEnabled = false;
        private bool isXIVRActive = false;
        private bool isXIVRCapital = false;
        private int cfgVersionValue = 9;
        private bool isVertMovement = false;
        private int timeOutCount = 0;
        private GroupType prevGroup = GroupType.Standard;
        private GroupType currentGroup = GroupType.Standard;
        bool isInDuty = false;

        public Plugin()
        {
            cfg = iPluginInterface!.GetPluginConfig() as Configuration ?? new Configuration();
            cfg.Initialize(iPluginInterface!);
            cfg.CheckVersion(cfgVersionValue);
            //cfg.SaveCurrent();

            //chatHandler = new XivCommonBase(iPluginInterface);
            MainWindow = new MainWindow(this);
            WindowSystem.AddWindow(MainWindow);

            CommandManager!.RemoveHandler(CommandName);
            CommandManager!.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Opens the settings window"
            });

            ClientState!.Login += OnLogin;
            ClientState!.Logout += OnLogout;
            iFramework!.Update += Update;
            iPluginInterface!.UiBuilder.Draw += DrawUI;
            iPluginInterface!.UiBuilder.OpenConfigUi += ToggleUI;
            iPluginInterface!.UiBuilder.OpenMainUi += ToggleUI;

            Initialize();
            Start();
        }

        public void Dispose()
        {
            Stop();

            ClientState!.Login -= OnLogin;
            ClientState!.Logout -= OnLogout;
            iFramework!.Update -= Update;
            iPluginInterface!.UiBuilder.Draw -= DrawUI;
            iPluginInterface!.UiBuilder.OpenConfigUi -= ToggleUI;
            iPluginInterface!.UiBuilder.OpenMainUi -= ToggleUI;

            CommandManager!.RemoveHandler(CommandName);

            smm.SetClose(SharedMemoryPlugins.ConvenientGraphics);
            smm.Dispose();
            cfgManager.Dispose();

            WindowSystem.RemoveAllWindows();
            MainWindow.Dispose();
        }

        public void ToggleUI() => MainWindow.IsOpen ^= true;
        private void OnCommand(string command, string argument)
        {
            if (string.IsNullOrEmpty(argument))
            {
                ToggleUI();
                return;
            }

            var regex = Regex.Match(argument, "^(\\w+) ?(.*)");
            var subcommand = regex.Success && regex.Groups.Count > 1 ? regex.Groups[1].Value : string.Empty;

            switch (subcommand.ToLower())
            {
                case "reset":
                    {
                        cfg.ResetDefaults();
                        break;
                    }
                case "copyconfig":
                    {
                        cfgManager.Save();
                        break;
                    }
                case "compareconfig":
                    {
                        cfgManager.Save(true);
                        break;
                    }
            }
        }

        private void DrawUI() => WindowSystem.Draw();

        private void OnLogin()
        {
            Start();
        }
        private void OnLogout(int type, int code)
        {

        }

        private void Update(IFramework framework)
        {
            //----
            // If vr is active, load vr settings
            // otherwise check and see if were in a duty and set that
            //----
            if (smm.CheckOpen(SharedMemoryPlugins.XIVR))
            {
                bool xivrActive = smm.CheckActive(SharedMemoryPlugins.XIVR);
                if (isXIVRActive != xivrActive)
                {
                    if (xivrActive)
                    {
                        prevGroup = currentGroup;
                        bool xivrCapital = cityZones.Contains(ClientState!.TerritoryType);
                        if (xivrCapital)
                            currentGroup = GroupType.VRInCapital;
                        else
                            currentGroup = GroupType.VR;
                        isXIVRCapital = xivrCapital;
                    }
                    else
                    {
                        currentGroup = prevGroup;
                        prevGroup = GroupType.Standard;
                        isXIVRCapital = false;
                    }
                    isXIVRActive = xivrActive;
                    SetSettings(currentGroup, cfg.GraphicsSettings[currentGroup]);
                }
            }
            if (!isXIVRActive)
            {
                isXIVRCapital = false;
                bool inDuty = Condition![ConditionFlag.BoundByDuty] || Condition![ConditionFlag.BoundByDuty95] || Condition![ConditionFlag.InDeepDungeon];
                if (isInDuty != inDuty)
                {
                    if (inDuty)
                        currentGroup = GroupType.InDuty;
                    else
                        currentGroup = GroupType.Standard;
                    isInDuty = inDuty;
                    SetSettings(currentGroup, cfg.GraphicsSettings[currentGroup]);
                }
            }
            else
            {
                bool xivrCapital = cityZones.Contains(ClientState!.TerritoryType);
                if (isXIVRCapital != xivrCapital)
                {
                    if(xivrCapital)
                        currentGroup = GroupType.VRInCapital;
                    else
                        currentGroup = GroupType.VR;
                    isXIVRCapital = xivrCapital;
                    SetSettings(currentGroup, cfg.GraphicsSettings[currentGroup]);
                }

                bool vertMovment = Condition![ConditionFlag.InFlight] || Condition![ConditionFlag.Diving];
                if (isVertMovement != vertMovment)
                {
                    if (vertMovment)
                        cfgManager.SetSettingsValue("MoveMode", 0);
                    else
                        cfgManager.SetSettingsValue("MoveMode", cfg.GraphicsSettings[GroupType.VR]["MoveMode"]);
                    isVertMovement = vertMovment;
                }
            }
        }

        public static void PrintEcho(string message) => ChatGui!.Print($"[ConvenientGraphics] {message}");
        public static void PrintError(string message) => ChatGui!.PrintError($"[ConvenientGraphics] {message}");

        private void Initialize()
        {
            smm.SetOpen(SharedMemoryPlugins.ConvenientGraphics);
            currentGroup = GroupType.Standard;
            
            List<string> cfgSearchStrings = new List<string>() {
                "Fps",
                "MouseOpeLimit",
                "Gamma",
                "CharaLight",
                "DisplayObjectLimitType",
                "TextureAnisotropicQuality_DX11",
                "SSAO_DX11",
                "Vignetting_DX11",
                "GrassQuality_DX11",
                "ShadowLOD_DX11",
                "ShadowVisibilityTypeOther_DX11",
                "ShadowVisibilityTypeEnemy_DX11",
                "PhysicsTypeSelf_DX11",
                "PhysicsTypeParty_DX11",
                "PhysicsTypeOther_DX11",
                "PhysicsTypeEnemy_DX11",
                "ReflectionType_DX11",
                "ParallaxOcclusion_DX11",
                "DynamicRezoThreshold",
                "GraphicsRezoScale",
                "GraphicsRezoUpscaleType",
                "ShadowBgLOD",
                "DynamicRezoType",
                "BattleEffectSelf",
                "BattleEffectParty",
                "BattleEffectOther",
                "BattleEffectPvPEnemyPc",
                "FPSCameraInterpolationType",
                "EventCameraAutoControl",
                "NamePlateDispTypeOther",
                "AutoFaceTargetOnAction",
                "ObjectBorderingType",
                "MoveMode",
                };
            //cfgManager.AddToList("charaLight");
            cfgManager.AddToList(cfgSearchStrings);
            cfgManager.MapSettings();
            //cfgManager.DebugSettings();

        }

        public void Start()
        {
            isEnabled = true;
            smm.SetActive(SharedMemoryPlugins.ConvenientGraphics);
            //PrintEcho("Graphics Changing Enabled");
            timeOutCount = 0;
            SetSettings(currentGroup, cfg.GraphicsSettings[currentGroup]);
        }

        public void Stop()
        {
            //PrintEcho("Graphics Changing Disabled");
            smm.SetInactive(SharedMemoryPlugins.ConvenientGraphics);
            isEnabled = false;
        }

        public void Reset()
        {
            timeOutCount = 0;
            SetSettings(currentGroup, cfg.GraphicsSettings[currentGroup]);
        }

        private void SetSettings(GroupType currentGroup, Dictionary<string, uint> settingOptions)
        {
            PrintEcho($"Setting {currentGroup}");
            foreach (KeyValuePair<string, uint> option in settingOptions)
            {
                if(!option.Key.StartsWith("_"))
                    cfgManager.SetSettingsValue(option.Key, option.Value);
            }

            //----
            // Set FPS
            //----
            if(settingOptions.ContainsKey("_UseChillframes"))
                if(settingOptions["_UseChillframes"] == 0)
                    CommandManager!.ProcessCommand($"/chillframes disable");
                else
                    CommandManager!.ProcessCommand($"/chillframes enable");

            //----
            // Set HUD
            //----
            if (settingOptions.ContainsKey("_SetHudLayout"))
            {
                //----
                // Create a timer to check for the existance of the player
                // and only update the hud if one exists
                //----
                uint hudLayout = settingOptions["_SetHudLayout"];
                if (hudLayout > 0)
                {
                    System.Timers.Timer hudChangeTimer = new System.Timers.Timer();
                    hudChangeTimer.Interval = 500;
                    hudChangeTimer.Elapsed += (sender, e) => { RefreshHUDLayoutTick(hudChangeTimer, hudLayout); };
                    hudChangeTimer.Enabled = true;
                    timeOutCount = 0;
                }
            }
        }

        public void RefreshHUDLayoutTick(System.Timers.Timer timer, uint hudLayout)
        {
            AtkUnitBase* hpBar = (AtkUnitBase*)GameGui!.GetAddonByName("_ParameterWidget", 1);
            AtkUnitBase* minimap = (AtkUnitBase*)GameGui!.GetAddonByName("_NaviMap", 1);

            timeOutCount++;
            if(hpBar->IsVisible || minimap->IsVisible)
            {
                timer.Enabled = false;
                hudLayout = Math.Max(Math.Min(hudLayout, 4), 1);
                //string cleanCmd = chatHandler.Functions.Chat.SanitiseText($"/hudlayout {hudLayout}");
                //chatHandler.Functions.Chat.SendMessage(cleanCmd);
            }
            else if (timeOutCount > 20)
                timer.Enabled = false;
        }
    }
}
