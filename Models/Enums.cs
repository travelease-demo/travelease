namespace TravelEase.Models;

public enum BookingStatus
{
    Pending = 0,
    Confirmed = 1,
    Cancelled = 2
}

public enum TripStatus
{
    Scheduled = 0,
    Delayed = 1,
    Completed = 2,
    Cancelled = 3
}

public enum TransportType
{
    Flight = 0,
    Train = 1,
    Bus = 2,
    Cruise = 3
}
