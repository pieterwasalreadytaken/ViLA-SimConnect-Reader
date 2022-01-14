using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SimConnectReader.SimConnectFSX
{
    public static class Extensions
    {
        public static string ToSimConnectString(this TOGGLE_VALUE value)
        {
            return value.ToString().Replace("__", ":").Replace('_', ' ');
        }
    }
}
