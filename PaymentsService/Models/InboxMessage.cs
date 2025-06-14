using System.ComponentModel.DataAnnotations;

namespace PaymentsService.Models
{
    public class InboxMessage
    {
        [Key]
        public Guid MessageId { get; set; }

        [Required]
        public string MessageType { get; set; } = null!;

        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ProcessedAt { get; set; }

        public string? ProcessingError { get; set; }
    }
}
