using System.ComponentModel.DataAnnotations;

namespace TravelEase.Models.ViewModels;

/// <summary>Input model for creating / editing a customer (prevents over-posting of entity fields).</summary>
public class CustomerFormModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "First name is required.")]
    [StringLength(60, MinimumLength = 2, ErrorMessage = "First name must be 2–60 characters.")]
    [Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Last name is required.")]
    [StringLength(60, MinimumLength = 1, ErrorMessage = "Last name must be 1–60 characters.")]
    [Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(150)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Phone number is required.")]
    [RegularExpression(@"^\+?[0-9][0-9\s\-()]{6,20}$", ErrorMessage = "Enter a valid phone number, e.g. +91 98200 12345.")]
    [Display(Name = "Phone number")]
    public string Phone { get; set; } = string.Empty;

    [StringLength(80)]
    public string? City { get; set; }

    [StringLength(80)]
    public string? Country { get; set; }

    public bool IsEdit => Id.HasValue;

    public static CustomerFormModel From(Customer c) => new()
    {
        Id = c.Id,
        FirstName = c.FirstName,
        LastName = c.LastName,
        Email = c.Email,
        Phone = c.Phone,
        City = c.City,
        Country = c.Country
    };
}

public class TripFormModel : IValidatableObject
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Trip code is required.")]
    [StringLength(20)]
    [RegularExpression(@"^[A-Za-z0-9\-]+$", ErrorMessage = "Use letters, numbers and dashes only.")]
    [Display(Name = "Trip code")]
    public string TripCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Source is required.")]
    [StringLength(80)]
    [Display(Name = "From (source)")]
    public string Source { get; set; } = string.Empty;

    [Required(ErrorMessage = "Destination is required.")]
    [StringLength(80)]
    [Display(Name = "To (destination)")]
    public string Destination { get; set; } = string.Empty;

    [Display(Name = "Transport")]
    public TransportType TransportType { get; set; } = TransportType.Flight;

    [StringLength(80)]
    [Display(Name = "Operator / carrier")]
    public string? Operator { get; set; }

    [Required]
    [Display(Name = "Departure")]
    public DateTime DepartureDate { get; set; } = DateTime.UtcNow.Date.AddDays(14).AddHours(9);

    [Required]
    [Display(Name = "Arrival")]
    public DateTime ArrivalDate { get; set; } = DateTime.UtcNow.Date.AddDays(14).AddHours(12);

    [Range(1, 1000, ErrorMessage = "Seats must be between 1 and 1000.")]
    [Display(Name = "Total seats")]
    public int TotalSeats { get; set; } = 60;

    [Range(typeof(decimal), "1", "10000000", ErrorMessage = "Ticket price must be greater than zero.")]
    [Display(Name = "Ticket price")]
    public decimal TicketPrice { get; set; }

    public TripStatus Status { get; set; } = TripStatus.Scheduled;

    [StringLength(500)]
    public string? Description { get; set; }

    public bool IsEdit => Id.HasValue;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ArrivalDate <= DepartureDate)
        {
            yield return new ValidationResult("Arrival must be after departure.", new[] { nameof(ArrivalDate) });
        }

        if (string.Equals(Source?.Trim(), Destination?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            yield return new ValidationResult("Source and destination must be different.", new[] { nameof(Destination) });
        }
    }

    public static TripFormModel From(Trip t) => new()
    {
        Id = t.Id,
        TripCode = t.TripCode,
        Source = t.Source,
        Destination = t.Destination,
        TransportType = t.TransportType,
        Operator = t.Operator,
        DepartureDate = t.DepartureDate,
        ArrivalDate = t.ArrivalDate,
        TotalSeats = t.TotalSeats,
        TicketPrice = t.TicketPrice,
        Status = t.Status,
        Description = t.Description
    };
}

public class BookingCreateModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Select a customer.")]
    [Display(Name = "Customer")]
    public int CustomerId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Select a trip.")]
    [Display(Name = "Trip")]
    public int TripId { get; set; }

    [Range(1, 20, ErrorMessage = "Passengers must be between 1 and 20.")]
    [Display(Name = "Passengers")]
    public int Passengers { get; set; } = 1;

    [Display(Name = "Confirm immediately")]
    public bool ConfirmImmediately { get; set; } = true;

    [StringLength(500)]
    public string? Notes { get; set; }
}

public class BookingCancelModel
{
    public int Id { get; set; }
    public string? BookingReference { get; set; }

    [Required(ErrorMessage = "Please give a reason for the cancellation.")]
    [StringLength(300, MinimumLength = 3, ErrorMessage = "Reason must be 3–300 characters.")]
    [Display(Name = "Cancellation reason")]
    public string Reason { get; set; } = string.Empty;
}
