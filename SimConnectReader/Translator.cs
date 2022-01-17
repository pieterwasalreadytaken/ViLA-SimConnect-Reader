using Microsoft.Extensions.Logging;
using SimConnectReader.SimConnectFSX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ViLA.Extensions.SimConnectReader
{
    internal class Translator
    {
        private ILogger<Translator> logger;
        private List<string> errors = new List<string>();
        internal Translator(ILogger<Translator> log)
        {
            logger = log;
        }

        internal List<TOGGLE_VALUE> convertFromString(IReadOnlyCollection<string> triggerList)
        {
            var result = new List<TOGGLE_VALUE>();
            errors.Clear();

            foreach(string variable in triggerList)
            {
                var validValue = FromString(variable);
                if (validValue != null)
                {
                    result.Add((TOGGLE_VALUE)validValue);
                }
            }

            logger.LogInformation("Invalid (or unknown) SimConnect variables in config file: " + string.Join(", ", errors));
            return result;
        }

        private TOGGLE_VALUE? FromString(string input)
        {
            try
            {
                TOGGLE_VALUE result = (TOGGLE_VALUE)Enum.Parse(typeof(TOGGLE_VALUE), input.Replace(" ", "_").Replace(":", "__"));
                return result;
            }
            catch
            {
                errors.Add(input);
                return null;
            }
        }
    }
}
