using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TAS.Input {
    [Flags]
    public enum Actions {
        None = 0,
        Left = 1 << 0,
        Right = 1 << 1,
        Up = 1 << 2,
        Down = 1 << 3,
        Jump = 1 << 4,
        Dash = 1 << 5,
        Grab = 1 << 6,
        Start = 1 << 7,
        Restart = 1 << 8,
        Feather = 1 << 9,
        Journal = 1 << 10,
        Jump2 = 1 << 11,
        Dash2 = 1 << 12,
        Confirm = 1 << 13,
        DemoDash = 1 << 14
    }

    public class InputFrame {
        static private Regex fractional = new Regex(@"\d+\.(\d*)", RegexOptions.Compiled);

        public Actions Actions;
        public float Angle;
        public int Frames;
        public int Line;
        public float Precision;

        public bool HasActions(Actions actions) =>
            (Actions & actions) != 0;

        public float GetX() =>
            (float) Math.Sin(Angle * Math.PI / 180.0);

        public float GetY() =>
            (float) Math.Cos(Angle * Math.PI / 180.0);

        public override string ToString() {
            StringBuilder sb = new StringBuilder();
            sb.Append(Frames);
            if (HasActions(Actions.Left)) {
                sb.Append(",L");
            }

            if (HasActions(Actions.Right)) {
                sb.Append(",R");
            }

            if (HasActions(Actions.Up)) {
                sb.Append(",U");
            }

            if (HasActions(Actions.Down)) {
                sb.Append(",D");
            }

            if (HasActions(Actions.Jump)) {
                sb.Append(",J");
            }

            if (HasActions(Actions.Jump2)) {
                sb.Append(",K");
            }

            if (HasActions(Actions.DemoDash)) {
                sb.Append(",Z");
            }

            if (HasActions(Actions.Dash)) {
                sb.Append(",X");
            }

            if (HasActions(Actions.Dash2)) {
                sb.Append(",C");
            }

            if (HasActions(Actions.Grab)) {
                sb.Append(",G");
            }

            if (HasActions(Actions.Start)) {
                sb.Append(",S");
            }

            if (HasActions(Actions.Restart)) {
                sb.Append(",Q");
            }

            if (HasActions(Actions.Journal)) {
                sb.Append(",N");
            }

            if (HasActions(Actions.Confirm)) {
                sb.Append(",O");
            }

            if (HasActions(Actions.Feather)) {
                sb.Append(",F,").Append(Angle == 0 ? string.Empty : Angle.ToString(CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        public InputFrame Clone() {
            InputFrame clone = new InputFrame {
                Frames = Frames,
                Actions = Actions,
                Angle = Angle,
                Line = Line,
            };
            return clone;
        }

        public static bool TryParse(string line, int studioLine, out InputFrame inputFrame) {
            int index = line.IndexOf(",", StringComparison.Ordinal);
            string framesStr;
            if (index == -1) {
                framesStr = line;
                index = 0;
            } else {
                framesStr = line.Substring(0, index);
            }

            if (!int.TryParse(framesStr, out int frames)) {
                inputFrame = null;
                return false;
            }

            frames = Math.Min(frames, 9999);
            inputFrame = new InputFrame {Line = studioLine, Frames = frames};
            while (index < line.Length) {
                char c = line[index];

                switch (char.ToUpper(c)) {
                    case 'L':
                        inputFrame.Actions ^= Actions.Left;
                        break;
                    case 'R':
                        inputFrame.Actions ^= Actions.Right;
                        break;
                    case 'U':
                        inputFrame.Actions ^= Actions.Up;
                        break;
                    case 'D':
                        inputFrame.Actions ^= Actions.Down;
                        break;
                    case 'J':
                        inputFrame.Actions ^= Actions.Jump;
                        break;
                    case 'X':
                        inputFrame.Actions ^= Actions.Dash;
                        break;
                    case 'G':
                        inputFrame.Actions ^= Actions.Grab;
                        break;
                    case 'S':
                        inputFrame.Actions ^= Actions.Start;
                        break;
                    case 'Q':
                        inputFrame.Actions ^= Actions.Restart;
                        break;
                    case 'N':
                        inputFrame.Actions ^= Actions.Journal;
                        break;
                    case 'K':
                        inputFrame.Actions ^= Actions.Jump2;
                        break;
                    case 'C':
                        inputFrame.Actions ^= Actions.Dash2;
                        break;
                    case 'O':
                        inputFrame.Actions ^= Actions.Confirm;
                        break;
                    case 'Z':
                        inputFrame.Actions ^= Actions.DemoDash;
                        break;
                    case 'F':
                        inputFrame.Actions ^= Actions.Feather;
                        index++;
                        string angle = line.Substring(index + 1);
                        if (angle == "") {
                            inputFrame.Angle = 0;
                            inputFrame.Precision = 1E-6f;
                        } else {
                            inputFrame.Angle = float.Parse(angle.Trim());
                            int digits = 0;
                            MatchCollection match=fractional.Matches(angle);
                            if (match.Count != 0) {
                                Match mat = match[0];
                                digits = mat.Groups[0].Value.Length;
                            }
                            inputFrame.Precision = float.Parse($"0.5E-{digits+2}");
                        }
                        continue;
                }

                index++;
            }

            return true;
        }
    }
}