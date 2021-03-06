using System;

namespace TAS.Input {
    public class Command {
        private readonly Action commandCall;
        public readonly int Frame;
        public readonly string LineText;

        public Command(int frame, Action commandCall, string lineText) {
            this.Frame = frame;
            this.commandCall = commandCall;
            this.LineText = lineText;
        }

        public void Invoke() => commandCall.Invoke();

        public Command Clone() => new Command(Frame, commandCall, LineText);
    }
}