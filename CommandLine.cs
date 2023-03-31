using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MakeAlbumn
{
    public class CommandLine
    {
        readonly List<string> _parameters;
        readonly Dictionary<string, string> _switches;

        public CommandLine(IEnumerable<string> args)
        {
            _switches = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
            _parameters = new List<string>();
            foreach (var arg in args)
            {
                if (!arg.StartsWith("/"))
                    _parameters.Add(arg);
                else
                {
                    var swtch = arg.Substring(1);
                    var index = swtch.IndexOf(':');
                    if (index == -1)
                        _switches[swtch] = string.Empty;
                    else
                    {
                        _switches[swtch.Substring(0, index)]
                            = swtch.Substring(index + 1, swtch.Length - index - 1);
                    }
                }
            }
        }

        public string Parameter(int index)
        {
            return index >= _parameters.Count ? string.Empty : _parameters[index];
        }

        public int ParameterCount
        {
            get { return _parameters.Count; }
        }

        public bool IsSwitchOn(string switchName)
        {
            return _switches.ContainsKey(switchName);
        }

        public string Switch(string switchName, Func<string, bool> validator = null)
        {
            if (_switches.ContainsKey(switchName))
            {
                if (validator != null && !validator(_switches[switchName]))
                    return null;
                return _switches[switchName];
            }
            return null;
        }

        public int IntSwitch(string switchName, int defaultValue, Func<int, bool> validator = null)
        {
            var swValue = Switch(switchName);
            if (int.TryParse(swValue, out int result))
            {
                if (validator != null && !validator(result))
                    return defaultValue;
                return result;
            }
            return defaultValue;
        }
    }
}
