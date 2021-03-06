using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework.Input;
using Monocle;
using TAS.EverestInterop;
using WinForms = System.Windows.Forms;

namespace TAS.StudioCommunication {
    public sealed class StudioCommunicationClient : StudioCommunicationBase {
        public static StudioCommunicationClient Instance;

        private StudioCommunicationClient() { }
        private StudioCommunicationClient(string target) : base(target) { }

        public static bool Run() {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) {
                return false;
            }

            Instance = new StudioCommunicationClient();

#if DEBUG
            //SetupDebugVariables();
#endif

            RunThread.Start(Setup, "StudioCom Client");

            void Setup() {
                Celeste.Celeste.Instance.Exiting -= Destroy;
                Celeste.Celeste.Instance.Exiting += Destroy;
                Instance.UpdateLoop();
            }

            return true;
        }

        public static void Destroy(object sender = null, EventArgs e = null) {
            if (Instance != null) {
                Instance.Abort = true;
                Instance = null;
            }
        }

        /// <summary>
        /// Do not use outside of multiplayer mods. Allows more than 2 processes to communicate.
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        public static StudioCommunicationClient RunExternal(string target) {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) {
                return null;
            }

            var client = new StudioCommunicationClient(target);

            RunThread.Start(Instance.UpdateLoop, "StudioCom Client_" + target);

            return client;
        }


        private static void SetupDebugVariables() {
            Hotkeys.KeysList = new List<Keys>[] {
                new List<Keys> {Keys.RightControl, Keys.OemOpenBrackets}, new List<Keys> {Keys.RightControl, Keys.RightShift},
                new List<Keys> {Keys.OemOpenBrackets}, new List<Keys> {Keys.OemCloseBrackets}, new List<Keys> {Keys.V}, new List<Keys> {Keys.B},
                new List<Keys> {Keys.N}
            };
        }

        #region Read

        protected override void ReadData(Message message) {
            switch (message.Id) {
                case MessageIDs.EstablishConnection:
                    throw new NeedsResetException("Initialization data recieved in main loop");
                case MessageIDs.Wait:
                    ProcessWait();
                    break;
                case MessageIDs.GetConsoleCommand:
                    ProcessGetConsoleCommand();
                    break;
                case MessageIDs.GetModInfo:
                    ProcessGetModInfo();
                    break;
                case MessageIDs.SendPath:
                    ProcessSendPath(message.Data);
                    break;
                case MessageIDs.SendHotkeyPressed:
                    ProcessHotkeyPressed(message.Data);
                    break;
                case MessageIDs.SendNewBindings:
                    ProcessNewBindings(message.Data);
                    break;
                case MessageIDs.ReloadBindings:
                    ProcessReloadBindings(message.Data);
                    break;
                default:
                    if (ExternalReadHandler?.Invoke(message.Data) != true) {
                        throw new InvalidOperationException($"{message.Id}");
                    }

                    break;
            }
        }

        private void ProcessGetConsoleCommand() {
            string command = Input.ConsoleHandler.CreateConsoleCommand();
            if (command != null) {
                byte[] commandBytes = Encoding.Default.GetBytes(command);
                WriteMessageGuaranteed(new Message(MessageIDs.ReturnConsoleCommand, commandBytes));
            }
        }

        private void ProcessGetModInfo() {
            if (Engine.Scene is Level level) {
                string MetaToString(EverestModuleMetadata metadata, int Indentation = 0, bool comment = true) {
                    return (comment ? "# " : string.Empty) + string.Empty.PadLeft(Indentation) + $"{metadata.Name} {metadata.VersionString}\n";
                }

                List<EverestModuleMetadata> metas = Everest.Modules
                    .Where(module => module.Metadata.Name != "UpdateChecker" && module.Metadata.Name != "DialogCutscene")
                    .Select(module => module.Metadata).ToList();
                metas.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

                AreaData areaData = AreaData.Get(level);
                string moduleName = string.Empty;
                EverestModuleMetadata mapMeta = null;
                if (Everest.Content.TryGet<AssetTypeMap>("Maps/" + areaData.SID, out ModAsset mapModAsset) && mapModAsset.Source != null) {
                    moduleName = mapModAsset.Source.Name;
                    mapMeta = metas.FirstOrDefault(meta => meta.Name == moduleName);
                }

                string command = "";

                EverestModuleMetadata celesteMeta = metas.First(metadata => metadata.Name == "Celeste");
                EverestModuleMetadata everestMeta = metas.First(metadata => metadata.Name == "Everest");
                EverestModuleMetadata tasMeta = metas.First(metadata => metadata.Name == "CelesteTAS");
                command += MetaToString(celesteMeta);
                command += MetaToString(everestMeta);
                command += MetaToString(tasMeta);
                metas.Remove(celesteMeta);
                metas.Remove(everestMeta);
                metas.Remove(tasMeta);

                EverestModuleMetadata speedrunToolMeta = metas.FirstOrDefault(metadata => metadata.Name == "SpeedrunTool");
                if (speedrunToolMeta != null) {
                    command += MetaToString(speedrunToolMeta);
                }

                command += "\n# Map:\n";
                if (mapMeta != null) {
                    command += MetaToString(mapMeta, 2);
                }

                string mode = level.Session.Area.Mode == AreaMode.Normal ? "ASide" : level.Session.Area.Mode.ToString();
                command += $"#   {areaData.SID} {mode}\n";

                if (!string.IsNullOrEmpty(moduleName) && mapMeta != null) {
                    List<EverestModuleMetadata> dependencies = mapMeta.Dependencies.Where(metadata =>
                        metadata.Name != "Celeste" && metadata.Name != "Everest" && metadata.Name != "UpdateChecker" &&
                        metadata.Name != "DialogCutscene" && metadata.Name != "CelesteTAS").ToList();
                    dependencies.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                    if (dependencies.Count > 0) {
                        command += "\n# Dependencies:\n";
                        command += string.Join(string.Empty,
                            dependencies.Select(meta => metas.First(metadata => metadata.Name == meta.Name)).Select(meta => MetaToString(meta, 2)));
                    }

                    command += "\n# Other Installed Mods:\n";
                    command += string.Join(string.Empty,
                        metas.Where(meta => meta.Name != moduleName && dependencies.All(metadata => metadata.Name != meta.Name))
                            .Select(meta => MetaToString(meta, 2)));
                } else {
                    command += "\n# Other Installed Mods:\n";
                    command += string.Join(string.Empty, metas.Select(meta => MetaToString(meta, 2)));
                }

                byte[] commandBytes = Encoding.Default.GetBytes(command);
                WriteMessageGuaranteed(new Message(MessageIDs.ReturnModInfo, commandBytes));
            }
        }

        private void ProcessSendPath(byte[] data) {
            string path = Encoding.Default.GetString(data);
            Log("ProcessSendPath: " + path);
            Manager.Settings.TasFilePath = path;
        }

        private void ProcessHotkeyPressed(byte[] data) {
            HotkeyIDs hotkey = (HotkeyIDs) data[0];
            bool released = Convert.ToBoolean(data[1]);
            if (released) {
                Log($"{hotkey.ToString()} released");
            } else {
                Log($"{hotkey.ToString()} pressed");
            }

            Hotkeys.HotkeyList[data[0]].OverridePressed = !released;
        }

        private void ProcessNewBindings(byte[] data) {
            byte id = data[0];
            List<Keys> keys = FromByteArray<List<Keys>>(data, 1);
            Log($"{((HotkeyIDs) id).ToString()} set to {keys}");
            Hotkeys.KeysList[id] = keys;
        }

        private void ProcessReloadBindings(byte[] data) {
            Log("Reloading bindings");
            Hotkeys.InputInitialize();
        }

        #endregion

        #region Write

        protected override void EstablishConnection() {
            var studio = this;
            var celeste = this;
            studio = null;

            Message? lastMessage;

            //Stall until input initialized to avoid sending invalid hotkey data
            while (Hotkeys.KeysList == null) {
                Thread.Sleep(Timeout);
            }

            studio?.WriteMessageGuaranteed(new Message(MessageIDs.EstablishConnection, new byte[0]));
            lastMessage = celeste?.ReadMessageGuaranteed();
            if (lastMessage?.Id != MessageIDs.EstablishConnection) {
                throw new NeedsResetException("Invalid data recieved while establishing connection");
            }

            celeste?.SendPath(Directory.GetCurrentDirectory());
            lastMessage = studio?.ReadMessageGuaranteed();
            //if (lastMessage?.ID != MessageIDs.SendPath)
            //	throw new NeedsResetException();
            studio?.ProcessSendPath(lastMessage?.Data);

            studio?.SendPath(null);
            lastMessage = celeste?.ReadMessageGuaranteed();
            if (lastMessage?.Id != MessageIDs.SendPath) {
                throw new NeedsResetException("Invalid data recieved while establishing connection");
            }

            celeste?.ProcessSendPath(lastMessage?.Data);

            celeste?.SendCurrentBindings(Hotkeys.KeysList);
            lastMessage = studio?.ReadMessageGuaranteed();
            //if (lastMessage?.ID != MessageIDs.SendCurrentBindings)
            //	throw new NeedsResetException();
            //studio?.ProcessSendCurrentBindings(lastMessage?.Data);

            Initialized = true;
        }

        private void SendPath(string path) {
            byte[] pathBytes = Encoding.Default.GetBytes(path);
            WriteMessageGuaranteed(new Message(MessageIDs.SendPath, pathBytes));
        }

        private void SendStateAndPlayerDataNow(string state, string playerData, bool canFail) {
            if (Initialized) {
                string[] data = new string[] {state, playerData};
                byte[] dataBytes = ToByteArray(data);
                Message message = new Message(MessageIDs.SendState, dataBytes);
                if (canFail) {
                    WriteMessage(message);
                } else {
                    WriteMessageGuaranteed(message);
                }
            }
        }

        public void SendStateAndPlayerData(string state, string playerData, bool canFail) {
            PendingWrite = () => SendStateAndPlayerDataNow(state, playerData, canFail);
        }

        private void SendCurrentBindings(List<Keys>[] bindings) {
            List<WinForms.Keys>[] nativeBindings = new List<WinForms.Keys>[bindings.Length];
            int i = 0;
            foreach (List<Keys> keys in bindings) {
                nativeBindings[i] = new List<WinForms.Keys>();
                foreach (Keys key in keys) {
                    nativeBindings[i].Add((WinForms.Keys) key);
                }

                i++;
            }

            byte[] data = ToByteArray(nativeBindings);
            WriteMessageGuaranteed(new Message(MessageIDs.SendCurrentBindings, data));
        }

        #endregion
    }
}