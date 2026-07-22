using System.ComponentModel.DataAnnotations;

namespace Basarsoft.Api.DTOs;

// Body for PUT /api/routes/{id}/stops/order. The full set of the route's stop ids in the new order.
// TransportationService requires the submitted ids to match the route's current stops exactly (same
// set, no repeats, no strangers) before renumbering their SequenceOrder to 1..N.
public class StopOrderRequest
{
    [Required]
    [MinLength(1)]
    public List<int> OrderedStopIds { get; set; } = new();
}
