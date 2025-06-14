using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace PaymentsService.Models
{
    public class OutboxMessage
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        public Guid CorrelationId { get; set; }

        [Required]
        public string MessageType { get; set; } = null!;

        [Required]
        public string Payload { get; set; } = null!;

        [Required]
        public string Destination { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? SentAt { get; set; }
    }
}
