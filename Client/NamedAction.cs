using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DarkMultiPlayer
{
    public class NamedAction
    {
        public readonly Action action;
        public readonly string name;

        public NamedAction(Action action)
        {
            this.action = action;
            this.name = action.Target.ToString() + "." + action.Method.Name;
        }
    }
}
