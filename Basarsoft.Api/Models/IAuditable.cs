namespace Basarsoft.Api.Models;

// Any entity that carries a "last edited" timestamp. AppDbContext.SaveChanges stamps ModifiedDate
// on every insert/update, so individual services never have to remember to set it.
public interface IAuditable
{
    DateTime ModifiedDate { get; set; }
}
