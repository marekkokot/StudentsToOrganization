using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace StudentsToOrganization.Models
{
    public class CopyModel
    {
        public int Group { get; set; }
        public int Section { get; set; }
        public string SrcRepo { get; set; }
        public string SrcDir { get; set; }
        public string DestDir { get; set; }
    }
}