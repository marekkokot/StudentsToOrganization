using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace StudentsToOrganization.Models
{
    public class ManageModel
    {
        public string CloneUrl { get; set; }
        public string Name { get; set; }   
        public string Surname { get; set; }
        public int Group { get; set; }
        public int Section { get; set; }
        public string TeamName { get; set; }
    }
}