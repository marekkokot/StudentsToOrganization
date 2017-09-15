using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Web;

namespace StudentsToOrganization.Models
{
    public class StudentData
    {
        [Required(ErrorMessage = "Pole {0} jest wymagane")]
        [DisplayName("git login")]

        public string GitLogin { get; set; }

        [Required(ErrorMessage = "Pole {0} jest wymagane")]
        [DisplayName("Imię")]
        public string FirstName { get; set; }

        //[DisplayName("Drugie Imię")]
        //public string SecondName { get; set; }

        [Required(ErrorMessage = "Pole {0} jest wymagane")]
        [DisplayName("Nazwisko")]
        public string Surname { get; set; }

        [Required(ErrorMessage = "Pole {0} jest wymagane")]
        [Range(1, 9)]
        [DisplayName("Grupa")]
        public int Group { get; set; }

        [Range(1, 9)]
        [Required(ErrorMessage = "Pole {0} jest wymagane")]
        [DisplayName("Sekcja")]
        public int Section { get; set; }
    }
}