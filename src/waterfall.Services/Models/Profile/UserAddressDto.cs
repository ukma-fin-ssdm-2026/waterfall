// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ComponentModel.DataAnnotations;

namespace Waterfall.Services.Models.Profile;

/// <summary>
/// DTO для передачі адреси користувача.
/// </summary>
public class UserAddressDto
{
    [MaxLength(300)]
    public string? Address { get; set; }
}
