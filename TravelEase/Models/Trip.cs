using System.ComponentModel.DataAnnotations;

namespace TravelEase.Models;

public class Trip
{
    public int Id { get; set; }

    [Required, StringLength(20)]
    public string TripCode { get; set; } = string.Empty;

    [Required, StringLength(80)]
    public string Source { get; set; } = string.Empty;

    [Required, StringLength(80)]
    public string Destination { get; set; } = string.Empty;

    public TransportType TransportType { get; set; } = TransportType.Flight;

    [StringLength(80)]
    public string? Operator { get; set; }

    public DateTime DepartureDate { get; set; }
    public DateTime ArrivalDate { get; set; }

    [Range(1, 1000)]
    public int TotalSeats { get; set; }

    [Range(0, 1000)]
    public int AvailableSeats { get; set; }

    [Range(0, 10_000_000)]
    public decimal TicketPrice { get; set; }

    public TripStatus Status { get; set; } = TripStatus.Scheduled;

    [StringLength(500)]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    public string Route => $"{Source} → {Destination}";
    public int BookedSeats => TotalSeats - AvailableSeats;
    public TimeSpan Duration => ArrivalDate - DepartureDate;

    public double OccupancyPercent => TotalSeats == 0 ? 0 : Math.Round(BookedSeats * 100.0 / TotalSeats, 1);

    public bool IsBookable =>
        Status is TripStatus.Scheduled or TripStatus.Delayed
        && AvailableSeats > 0
        && DepartureDate > DateTime.UtcNow;
}
