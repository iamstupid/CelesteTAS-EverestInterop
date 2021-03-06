using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.EverestInterop;
using TAS.Input;

namespace TAS {
    public static partial class Manager {
        private static StreamWriter sw;
        private static List<MethodInfo> trackedEntities;

        private static float framesPerSecond;

        //for debugging
        public static string AdditionalStatusInfo;

        public static bool ExportSyncData { get; set; }

        public static void UpdatePlayerInfo() {
            if (Engine.Scene is Level level) {
                Player player = level.Tracker.GetEntity<Player>();
                if (player != null) {
                    long chapterTime = level.Session.Time;
                    if (chapterTime != lastTimer || LastPos != player.ExactPosition) {
                        framesPerSecond = 60f / Engine.TimeRateB;
                        string pos = GetAdjustedPos(player.Position, player.PositionRemainder);
                        string speed = $"Speed: {player.Speed.X:F2}, {player.Speed.Y:F2}";
                        Vector2 diff = (player.ExactPosition - LastPos) * 60f;
                        string vel = $"Vel:   {diff.X:F2}, {diff.Y:F2}";
                        string polarvel = $"Fly:   {diff.Length():F2}, {GetAngle(diff):F5}°";

                        string joystick;
                        if (Running && Controller.Previous is InputFrame inputFrame && inputFrame.HasActions(Actions.Feather)) {
                            joystick =
                                $"Analog: {AnalogHelper.LastDirection.X:F5}, {AnalogHelper.LastDirection.Y:F5}, {GetAngle(new Vector2(AnalogHelper.LastDirection.X, -AnalogHelper.LastDirection.Y)):F5}°";
                        } else {
                            joystick = string.Empty;
                        }

                        string miscstats = $"Stamina: {player.Stamina:0}  "
                                           + (WallJumpCheck(player, 1) ? "Wall-R " : string.Empty)
                                           + (WallJumpCheck(player, -1) ? "Wall-L " : string.Empty);
                        int dashCooldown = (int) (DashCooldownTimer(player) * framesPerSecond);

                        PlayerSeeker playerSeeker = level.Entities.FindFirst<PlayerSeeker>();
                        if (playerSeeker != null) {
                            pos = GetAdjustedPos(playerSeeker.Position, playerSeeker.PositionRemainder);
                            speed =
                                $"Speed: {PlayerSeekerSpeed(playerSeeker).X:F2}, {PlayerSeekerSpeed(playerSeeker).Y:F2}";
                            diff = (playerSeeker.ExactPosition - LastPlayerSeekerPos) * 60f;
                            vel = $"Vel:   {diff.X:F2}, {diff.Y:F2}";
                            polarvel = $"Chase: {diff.Length():F2}, {GetAngle(diff):F2}°";
                            dashCooldown = (int) (PlayerSeekerDashTimer(playerSeeker) * framesPerSecond);
                        }

                        string statuses = (dashCooldown < 1 && player.Dashes > 0 ? "Dash " : string.Empty)
                                          + (player.LoseShards ? "Ground " : string.Empty)
                                          + (!player.LoseShards && JumpGraceTimer(player) > 0
                                              ? $"Coyote({(int) (JumpGraceTimer(player) * framesPerSecond)})"
                                              : string.Empty);
                        string transitionFrames = PlayerInfo.TransitionFrames > 0 ? $"({PlayerInfo.TransitionFrames})" : string.Empty;
                        statuses = (player.InControl && !level.Transitioning ? statuses : $"NoControl{transitionFrames} ")
                                   + (player.TimePaused ? "Paused " : string.Empty)
                                   + (level.InCutscene ? "Cutscene " : string.Empty)
                                   + (AdditionalStatusInfo ?? string.Empty);


                        if (player.Holding == null) {
                            foreach (Component component in level.Tracker.GetComponents<Holdable>()) {
                                Holdable holdable = (Holdable) component;
                                if (holdable.Check(player)) {
                                    statuses += "Grab ";
                                    break;
                                }
                            }
                        }

                        int berryTimer = -10;
                        Follower firstRedBerryFollower =
                            player.Leader.Followers.Find(follower => follower.Entity is Strawberry berry && !berry.Golden);
                        if (firstRedBerryFollower?.Entity is Strawberry firstRedBerry) {
                            object collectTimer;
                            if (firstRedBerry.GetType() == typeof(Strawberry)
                                || (collectTimer = StrawberryCollectTimer(firstRedBerry)) == null) {
                                // if this is a vanilla berry or a mod berry having no collectTimer, use the cached FieldInfo for Strawberry.collectTimer.
                                collectTimer = StrawberryCollectTimerFieldInfo.GetValue(firstRedBerry);
                            }

                            berryTimer = 9 - (int) Math.Round((float) collectTimer * framesPerSecond);
                        }

                        string timers = (berryTimer != -10
                                            ? berryTimer <= 9 ? $"BerryTimer: {berryTimer} " : $"BerryTimer: 9+{berryTimer - 9} "
                                            : string.Empty)
                                        + (dashCooldown != 0 ? $"DashTimer: {(dashCooldown).ToString()} " : string.Empty);
                        string roomNameAndTime =
                            $"[{level.Session.Level}] Timer: {(chapterTime / 10000000D):F3}({chapterTime / TimeSpan.FromSeconds(Engine.RawDeltaTime).Ticks})";

                        StringBuilder sb = new StringBuilder();
                        sb.AppendLine(pos);
                        sb.AppendLine(speed);
                        sb.AppendLine(vel);

                        if (player.StateMachine.State == Player.StStarFly
                            || playerSeeker != null
                            || SaveData.Instance.Assists.ThreeSixtyDashing
                            || SaveData.Instance.Assists.SuperDashing) {
                            sb.AppendLine(polarvel);
                        }

                        if (!string.IsNullOrEmpty(joystick)) {
                            sb.AppendLine(joystick);
                        }

                        sb.AppendLine(miscstats);
                        if (!string.IsNullOrEmpty(statuses)) {
                            sb.AppendLine(statuses);
                        }

                        if (!string.IsNullOrEmpty(timers)) {
                            sb.AppendLine(timers);
                        }

                        sb.Append(roomNameAndTime);
                        LastPos = player.ExactPosition;
                        LastPlayerSeekerPos = playerSeeker?.ExactPosition ?? default;
                        lastTimer = chapterTime;
                        PlayerStatus = sb.ToString().TrimEnd();
                    }
                } else {
                    PlayerStatus = level.InCutscene ? "Cutscene" : string.Empty;
                }
            } else if (Engine.Scene is SummitVignette summit) {
                PlayerStatus = "SummitVignette " + SummitVignetteReadyFieldInfo.GetValue(summit);
            } else if (Engine.Scene is Overworld overworld) {
                PlayerStatus = "Overworld " + overworld.ShowInputUI;
            } else if (Engine.Scene != null) {
                PlayerStatus = Engine.Scene.GetType().Name;
            }
        }

        private static string GetAdjustedPos(Vector2 intPos, Vector2 subpixelPos) {
            double x = intPos.X;
            double y = intPos.Y;
            double subX = subpixelPos.X;
            double subY = subpixelPos.Y;

            if (!Settings.RoundPosition) {
                return $"Pos:   {(x + subX):F12}, {(y + subY):F12}";
            }

            if (Math.Abs(subX) % 0.25 < 0.01 || Math.Abs(subX) % 0.25 > 0.24) {
                if (x > 0 || (x == 0 && subX > 0)) {
                    x += Math.Floor(subX * 100) / 100;
                } else {
                    x += Math.Ceiling(subX * 100) / 100;
                }
            } else {
                x += subX;
            }

            if (Math.Abs(subY) % 0.25 < 0.01 || Math.Abs(subY) % 0.25 > 0.24) {
                if (y > 0 || (y == 0 && subY > 0)) {
                    y += Math.Floor(subY * 100) / 100;
                } else {
                    y += Math.Ceiling(subY * 100) / 100;
                }
            } else {
                y += subY;
            }

            string pos = $"Pos:   {x:F2}, {y:F2}";
            return pos;
        }

        public static void BeginExport(string path, string[] tracked) {
            sw?.Dispose();
            sw = new StreamWriter(path);
            sw.WriteLine(string.Join("\t", "Line", "Inputs", "Frames", "Time", "Position", "Speed", "State", "Statuses", "Entities"));
            trackedEntities = new List<MethodInfo>();
            foreach (string typeName in tracked) {
                string fullTypeName = typeName.Contains("@") ? typeName.Replace("@", ",") : $"Celeste.{typeName}, Celeste";
                Type t = Type.GetType(fullTypeName);
                if (t != null) {
                    trackedEntities.Add(typeof(EntityList).GetMethod("FindAll")?.MakeGenericMethod(t));
                }
            }
        }

        public static void EndExport() {
            Engine.Scene.OnEndOfFrame += () => { sw?.Dispose(); };
        }

        public static void ExportPlayerInfo() {
            Engine.Scene.OnEndOfFrame += () => {
                if (Engine.Scene is Level level) {
                    Player player = level.Tracker.GetEntity<Player>();
                    if (player == null) {
                        return;
                    }

                    string time = (level.Session.Time / 10000000D).ToString("0.000");
                    double x = (double) player.X + player.PositionRemainder.X;
                    double y = (double) player.Y + player.PositionRemainder.Y;
                    string pos = x + ", " + y;
                    string speed = player.Speed.X + ", " + player.Speed.Y;

                    int dashCooldown = (int) (DashCooldownTimer(player) * framesPerSecond);
                    string statuses = (dashCooldown < 1 && player.Dashes > 0 ? "Dash " : string.Empty)
                                      + (player.LoseShards ? "Ground " : string.Empty)
                                      + (WallJumpCheck(player, 1) ? "Wall-R " : string.Empty)
                                      + (WallJumpCheck(player, -1) ? "Wall-L " : string.Empty)
                                      + (!player.LoseShards && JumpGraceTimer(player) > 0 ? "Coyote " : string.Empty);
                    statuses = (player.InControl && !level.Transitioning ? statuses : "NoControl ")
                               + (player.TimePaused ? "Paused " : string.Empty)
                               + (level.InCutscene ? "Cutscene " : string.Empty)
                               + (AdditionalStatusInfo ?? string.Empty);

                    if (player.Holding == null) {
                        foreach (Component component in level.Tracker.GetComponents<Holdable>()) {
                            Holdable holdable = (Holdable) component;
                            if (holdable.Check(player)) {
                                statuses += "Grab ";
                                break;
                            }
                        }
                    }

                    string output = string.Empty;
                    if (Controller.CurrentFrame > 0 && Controller.Inputs.Count > 0) {
                        output = string.Join("\t",
                            Controller.Previous.Line, Controller.Previous, Controller.CurrentFrame - 1, time, pos, speed,
                            (PlayerState) player.StateMachine.State,
                            statuses);
                    }

                    foreach (MethodInfo method in trackedEntities) {
                        if (method == null) {
                            continue;
                        }

                        IList entities = (IList) method.Invoke(level.Entities, null);
                        foreach (Entity entity in entities) {
                            if (entity is Actor actor) {
                                x = (double) actor.X + actor.PositionRemainder.X;
                                y = (double) actor.Y + actor.PositionRemainder.Y;
                                pos = x + ", " + y;
                            } else {
                                pos = entity.X + ", " + entity.Y;
                            }

                            output += $"\t{method.GetGenericArguments()[0].Name}: {pos}";
                        }
                    }

                    sw.WriteLine(output);
                } else {
                    string inputs = Controller.Current.ToString();
                    if (inputs.Length > 1) {
                        inputs = inputs.Substring(1);
                    }

                    string output = string.Join(" ", inputs, Controller.CurrentFrame, Engine.Scene.GetType().Name);
                    sw.WriteLine(output);
                }
            };
        }
    }

    internal enum PlayerState {
        Normal = Player.StNormal,
        Climb = Player.StClimb,
        Dash = Player.StDash,
        Swim = Player.StSwim,
        Boost = Player.StBoost,
        RedDash = Player.StRedDash,
        HitSquash = Player.StHitSquash,
        Launch = Player.StLaunch,
        Pickup = Player.StPickup,
        DreamDash = Player.StDreamDash,
        SummitLaunch = Player.StSummitLaunch,
        Dummy = Player.StDummy,
        IntroWalk = Player.StIntroWalk,
        IntroJump = Player.StIntroJump,
        IntroRespawn = Player.StIntroRespawn,
        IntroWakeUp = Player.StIntroWakeUp,
        BirdDashTutorial = Player.StBirdDashTutorial,
        Frozen = Player.StFrozen,
        ReflectionFall = Player.StReflectionFall,
        StarFly = Player.StStarFly,
        TempleFall = Player.StTempleFall,
        CassetteFly = Player.StCassetteFly,
        Attract = Player.StAttract,
        IntroMoonJump = Player.StIntroMoonJump,
        FlingBird = Player.StFlingBird,
        IntroThinkForABit = Player.StIntroThinkForABit
    }
}
