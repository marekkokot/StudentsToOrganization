using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Web;

namespace StudentsToOrganization.Models
{
    public class ManageReposModel
    {
        [DisplayName("Imię")]
        public string FirstName { get; set; }

        //[DisplayName("Drugie Imię")]
        //public string SecondName { get; set; }
        
        [DisplayName("Nazwisko")]
        public string Surname { get; set; }

        [DisplayName("Grupa")]
        public int? Group { get; set; }

        [DisplayName("Sekcja")]
        public int? Section { get; set; }
    }
}