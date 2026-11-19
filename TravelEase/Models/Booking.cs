using System.ComponentModel.DataAnnotations;

namespace TravelEase.Models;

public class Booking
{
    public int Id { get; set; }

    [Required, StringLength(20)]
    public string BookingReference { get; set; } = string.Empty;

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public int TripId { get; set; }
    public Trip? Trip { get; set; }

    [Range(1, 20)]
    public int Passengers { get; set; } = 1;

    /// <summary>Ticket price captured at booking time (trip prices may change later).</summary>
    public decimal UnitPrice { get; set; }

    public decimal TotalPrice { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Pending;

    public DateTime BookingDate { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    [StringLength(300)]
    public string? CancellationReason { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}
