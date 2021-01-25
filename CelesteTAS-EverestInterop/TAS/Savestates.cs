﻿using System;
using System.Collections;
using System.Linq;
using Celeste;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.EverestInterop;
using TAS.StudioCommunication;
using static TAS.Manager;

namespace TAS {
    static class Savestates {
        public static int SavedLine => SpeedrunToolInstalled.Value && IsSaved() ? savedLine ?? -1 : -1;
        public static Coroutine routine;
        private static InputController savedController;
        private static int? savedLine;
        private static string savedPlayerStatus;
        private static Vector2 savedLastPos;
        private static bool savedByBreakpoint;

        private static bool BreakpointHasBeenDeleted => IsSaved() && savedByBreakpoint && savedController.InputIndex < controller.inputs.Count &&
                   controller.inputs[savedController.InputIndex - 1].SaveState == false;

        private static readonly Lazy<bool> SpeedrunToolInstalled = new Lazy<bool>(() =>
            Type.GetType("Celeste.Mod.SpeedrunTool.SaveLoad.StateManager, SpeedrunTool") != null
        );

        private static bool IsSaved() {
            return StateManager.Instance.IsSaved && savedController != null;
        }

        public static void HandleSaveStates() {
            if (!SpeedrunToolInstalled.Value) return;

            if (Running && Hotkeys.hotkeySaveState.pressed && !Hotkeys.hotkeySaveState.wasPressed) {
                Save(false);
                return;
            }

            if (Hotkeys.hotkeyLoadState.pressed && !Hotkeys.hotkeyLoadState.wasPressed && !Hotkeys.hotkeySaveState.pressed) {
                Load();
                return;
            }

            if (Hotkeys.hotkeyClearState.pressed && !Hotkeys.hotkeyClearState.wasPressed && !Hotkeys.hotkeySaveState.pressed) {
                Clear();
                return;
            }

            if (BreakpointHasBeenDeleted) {
                Clear();
            }

            // save state when tas run to ***s breakpoint
            if (Running && controller.Previous != null && controller.Previous.SaveState && !controller.Previous.HasSavedState && controller.inputs.Where(record => record.SaveState).All(record => controller.Previous.Line >= record.Line)
                && controller.CurrentInputFrame == 1)  {
                Save(true);
                return;
            }

            // auto load state after starting tas
            if (Running && IsSaved() && Engine.Scene is Level && SavedLine > controller.Current.Line) {
                Load();
            }
        }

        private static IEnumerator WaitForSavingState(Action onComplete) {
            yield return null;
            onComplete();
        }

        private static void Save(bool breakpoint) {
            if (IsSaved()) {
                if (controller.CurrentFrame  == savedController.CurrentFrame) {
                    if (savedController.SavedChecksum == controller.Checksum(savedController)) {
                        state &= ~State.FrameStep;
                        nextState &= ~State.FrameStep;
                        return;
                    }
                }
            }

            if (!StateManager.Instance.SaveState()) return;

            if (breakpoint && controller.Previous?.SaveState == true) {
                controller.Previous.HasSavedState = true;
            }

            savedLine = controller.Current.Line - 1;
            if (breakpoint) {
                savedLine = controller.Previous.Line;
            }

            savedByBreakpoint = breakpoint;
            savedPlayerStatus = PlayerStatus;
            savedLastPos = LastPos;

            state |= State.FrameStep;
            nextState &= ~State.FrameStep;

            savedController = controller.Clone();

            routine = new Coroutine(WaitForSavingState(() => {
                if (!CelesteTASModule.Settings.PauseAfterLoadState || controller.HasFastForward) {
                    state &= ~State.FrameStep;
                    nextState &= ~State.FrameStep;
                }

                // SpeedrunTool v3.4.15 no longer save and then automatically load,
                // although tas can also continue to run without loading
                // but it is better to load state, if savestate desync occurs can be found faster
                Load(true);
            }));
        }

        private static void Load(bool forceLoad = false) {
            if (controller.fastForwards.Any(record => record.Line > SavedLine)) {
                state &= ~State.FrameStep;
            } else {
                state |= State.FrameStep;
            }
            nextState &= ~State.FrameStep;

            if (IsSaved()) {
                controller.AdvanceFrame(true);
                if (forceLoad || !BreakpointHasBeenDeleted && savedController.SavedChecksum == controller.Checksum(savedController)) {
                    if (!forceLoad && Running &&  controller.CurrentFrame == savedController.CurrentFrame) {
                        // Don't repeat load state, just play
                        state &= ~State.FrameStep;
                        return;
                    }
                    if (StateManager.Instance.LoadState()) {
                        if (!Running) EnableExternal();
                        LoadStateRoutine();
                        return;
                    }
                } else {
                    Clear();
                }
            }

            // If load state failed just playback normally
            PlayTAS();
        }

        private static void Clear() {
            StateManager.Instance.ClearState();
            routine = null;
            savedController = null;
            savedLine = null;
            savedPlayerStatus = null;
            savedLastPos = default;
            savedByBreakpoint = false;
            UpdateStudio();
        }

        private static void PlayTAS() {
            DisableExternal();
            EnableExternal();
        }

        private static void LoadStateRoutine() {
            controller = savedController.Clone();
            controller.AdvanceFrame(true);

            if (CelesteTASModule.Settings.PauseAfterLoadState && !controller.HasFastForward) {
                state |= State.FrameStep;
                nextState &= ~State.FrameStep;
            }
            PlayerStatus = savedPlayerStatus;
            LastPos = savedLastPos;
            UpdateStudio();
        }

        private static void UpdateStudio() {
            if (controller.Current != null) {
                CurrentStatus = controller.Current.Line + "[" + controller + "]" + SavedLine;
            }
            StudioCommunicationClient.instance?.SendStateAndPlayerData(CurrentStatus, PlayerStatus, false);
        }
    }
}