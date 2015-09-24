using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;

namespace StudentsToOrganization.Other
{
    public static class Extensions
    {
        public static string RemoveDiacritics(this string s)
        {
            string asciiEquivalents = Encoding.ASCII.GetString(
                         Encoding.GetEncoding("Cyrillic").GetBytes(s)
                     );

            return asciiEquivalents;
        }
    }
}