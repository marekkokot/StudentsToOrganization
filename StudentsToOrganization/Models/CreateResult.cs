using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Web;

namespace StudentsToOrganization.Models
{
    public class CreateResult
    {
        [DisplayName("Nazwa Team'u")]
        public string TeamName { get; set; }

        [DisplayName("Nazwa repozytorium")]
        public string RepoName { get; set; }

    }
}