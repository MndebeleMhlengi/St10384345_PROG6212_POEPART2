using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection.Metadata;

namespace CMCS.Models
{
    public class Claim
    {
        [Key]
        public int ClaimId { get; set; }

        [Required]
        public int LecturerId { get; set; }

        [Required]
        [Range(1, 12)]
        public int MonthWorked { get; set; }

        [Required]
        [Range(2020, 2030)]
        public int YearWorked { get; set; }

        [Required]
        [Range(0.1, 500)]
        public decimal HoursWorked { get; set; }

        [Required]
        [Range(0, 10000)]
        public decimal HourlyRate { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalAmount { get; set; }

        [StringLength(500)]
        public string AdditionalNotes { get; set; }

        public ClaimStatus Status { get; set; } = ClaimStatus.PENDING;

        public DateTime SubmissionDate { get; set; } = DateTime.Now;

        public DateTime LastModifiedDate { get; set; } = DateTime.Now;

        public bool IsActive { get; set; } = true;

        [StringLength(100)]
        public string ModuleTaught { get; set; }

        [StringLength(20)]
        public string ClaimReference { get; set; }

        // Navigation properties
        [ForeignKey("LecturerId")]
        public virtual User Lecturer { get; set; }

        public virtual ICollection<Document> Documents { get; set; }
        public virtual ICollection<ClaimApproval> Approvals { get; set; }
    }
}