using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace StudentsToOrganization.Models
{
    public class CreateIssueModel
    {
        [DisplayName("Grupa")]
        [Required(ErrorMessage = "Pole {0} jest wymagane")]
        public int Group { get; set; }

        [DisplayName("Sekcja")]
        [Required(ErrorMessage = "Pole {0} jest wymagane")]
        public int Section { get; set; }

        [DisplayName("Tytuł")]
        [AllowHtml]
        [Required(ErrorMessage = "Pole {0} jest wymagane")]
        public string Title { get; set; }

        [DisplayName("Treść")]
        [AllowHtml]
        public string Content { get; set; }        
    }
}