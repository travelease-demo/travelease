using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TravelEase.Data;
using TravelEase.Models;
using TravelEase.Models.ViewModels;

namespace TravelEase.Services;

public interface ICustomerService
{
    Task<CustomerIndexViewModel> GetIndexAsync(string? search, string? country, int page, int pageSize = 10, CancellationToken ct = default);
    Task<CustomerDetailsViewModel?> GetDetailsAsync(int id, CancellationToken ct = default);
    Task<Customer?> FindAsync(int id, CancellationToken ct = default);
    Task<ServiceResult<Customer>> CreateAsync(CustomerFormModel input, CancellationToken ct = default);
    Task<ServiceResult<Customer>> UpdateAsync(int id, CustomerFormModel input, CancellationToken ct = default);
    Task<ServiceResult> DeleteAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<SelectListItem>> GetSelectListAsync(int? selectedId = null, CancellationToken ct = default);
}

public sealed class CustomerService(TravelEaseDbContext db, ILogger<CustomerService> logger) : ICustomerService
{
    public async Task<CustomerIndexViewModel> GetIndexAsync(string? search, string? country, int page, int pageSize = 10, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        var query = db.Customers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(c =>
                EF.Functions.ILike(c.FirstName + " " + c.LastName, term) ||
                EF.Functions.ILike(c.Email, term) ||
                EF.Functions.ILike(c.Phone, term) ||
                (c.City != null && EF.Functions.ILike(c.City, term)));
        }

        if (!string.IsNullOrWhiteSpace(country))
        {
            query = query.Where(c => c.Country == country);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(c => c.FirstName).ThenBy(c => c.LastName)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new
            {
                c.Id, c.FirstName, c.LastName, c.Email, c.Phone, c.City, c.Country, c.CreatedAt,
                BookingCount = c.Bookings.Count,
                TotalSpent = c.Bookings.Where(b => b.Status == BookingStatus.Confirmed).Sum(b => (decimal?)b.TotalPrice) ?? 0
            })
            .ToListAsync(ct);

        var countries = await db.Customers.AsNoTracking()
            .Where(c => c.Country != null)
            .Select(c => c.Country!)
            .Distinct().OrderBy(c => c)
            .ToListAsync(ct);

        return new CustomerIndexViewModel
        {
            Search = search,
            Country = country,
            Countries = countries,
            Customers = new PagedResult<CustomerListItem>
            {
                Page = page,
                PageSize = pageSize,
                TotalCount = total,
                Items = items.Select(c => new CustomerListItem(
                    c.Id, $"{c.FirstName} {c.LastName}",
                    $"{c.FirstName[..1]}{c.LastName[..1]}".ToUpperInvariant(),
                    c.Email, c.Phone, c.City, c.Country, c.BookingCount, c.TotalSpent, c.CreatedAt)).ToList()
            }
        };
    }

    public async Task<CustomerDetailsViewModel?> GetDetailsAsync(int id, CancellationToken ct = default)
    {
        var customer = await db.Customers.AsNoTracking()
            .Include(c => c.Bookings.OrderByDescending(b => b.BookingDate))
            .ThenInclude(b => b.Trip)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (customer is null) return null;

        var confirmed = customer.Bookings.Where(b => b.Status == BookingStatus.Confirmed).ToList();
        return new CustomerDetailsViewModel
        {
            Customer = customer,
            TotalBookings = customer.Bookings.Count,
            ConfirmedBookings = confirmed.Count,
            TotalPassengers = customer.Bookings.Where(b => b.Status != BookingStatus.Cancelled).Sum(b => b.Passengers),
            TotalSpent = confirmed.Sum(b => b.TotalPrice)
        };
    }

    public Task<Customer?> FindAsync(int id, CancellationToken ct = default) =>
        db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<ServiceResult<Customer>> CreateAsync(CustomerFormModel input, CancellationToken ct = default)
    {
        var email = input.Email.Trim().ToLowerInvariant();
        if (await db.Customers.AnyAsync(c => c.Email == email, ct))
        {
            return ServiceResult<Customer>.Fail("A customer with this email address already exists.", nameof(input.Email));
        }

        var customer = new Customer();
        Apply(customer, input, email);
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Customer {CustomerId} created: {CustomerName} <{Email}>", customer.Id, customer.FullName, customer.Email);
        return ServiceResult<Customer>.Ok(customer);
    }

    public async Task<ServiceResult<Customer>> UpdateAsync(int id, CustomerFormModel input, CancellationToken ct = default)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null) return ServiceResult<Customer>.Missing("Customer not found.");

        var email = input.Email.Trim().ToLowerInvariant();
        if (await db.Customers.AnyAsync(c => c.Email == email && c.Id != id, ct))
        {
            return ServiceResult<Customer>.Fail("Another customer already uses this email address.", nameof(input.Email));
        }

        Apply(customer, input, email);
        customer.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Customer {CustomerId} updated", customer.Id);
        return ServiceResult<Customer>.Ok(customer);
    }

    public async Task<ServiceResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null) return ServiceResult.Missing("Customer not found.");

        var bookingCount = await db.Bookings.CountAsync(b => b.CustomerId == id, ct);
        if (bookingCount > 0)
        {
            return ServiceResult.Fail($"{customer.FullName} has {bookingCount} booking(s) and cannot be deleted. Booking history must be preserved.");
        }

        db.Customers.Remove(customer);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Customer {CustomerId} deleted ({CustomerName})", id, customer.FullName);
        return ServiceResult.Ok();
    }

    public async Task<IReadOnlyList<SelectListItem>> GetSelectListAsync(int? selectedId = null, CancellationToken ct = default)
    {
        var customers = await db.Customers.AsNoTracking()
            .OrderBy(c => c.FirstName).ThenBy(c => c.LastName)
            .Select(c => new { c.Id, c.FirstName, c.LastName, c.Email })
            .ToListAsync(ct);

        return customers.Select(c => new SelectListItem($"{c.FirstName} {c.LastName} — {c.Email}", c.Id.ToString(), c.Id == selectedId)).ToList();
    }

    private static void Apply(Customer customer, CustomerFormModel input, string normalisedEmail)
    {
        customer.FirstName = input.FirstName.Trim();
        customer.LastName = input.LastName.Trim();
        customer.Email = normalisedEmail;
        customer.Phone = input.Phone.Trim();
        customer.City = string.IsNullOrWhiteSpace(input.City) ? null : input.City.Trim();
        customer.Country = string.IsNullOrWhiteSpace(input.Country) ? null : input.Country.Trim();
    }
}
