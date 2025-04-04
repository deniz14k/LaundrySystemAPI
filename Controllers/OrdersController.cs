using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AplicatieSpalatorie.Data;
using AplicatieSpalatorie.Models;

namespace AplicatieSpalatorie.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public OrdersController(ApplicationDbContext context)
        {
            _context = context;
        }

        // --------------------------------------------------------------
        // 1) GET: api/orders?searchTerm=...&fromDate=...&toDate=...&status=...
        //    Returns all orders, optionally filtered by search/date/status.
        // --------------------------------------------------------------
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Order>>> GetOrders(
            [FromQuery] string? searchTerm,
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate,
            [FromQuery] string? status)
        {
            IQueryable<Order> query = _context.Orders
                                              .Include(o => o.Items);

            // (A) Apply filters if provided
            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(o => o.CustomerId.Contains(searchTerm) ||
                                         o.TelephoneNumber.Contains(searchTerm));
            }
            if (fromDate.HasValue)
            {
                query = query.Where(o => o.ReceivedDate >= fromDate.Value);
            }
            if (toDate.HasValue)
            {
                query = query.Where(o => o.ReceivedDate <= toDate.Value);
            }
            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(o => o.Status == status);
            }

            // (B) Execute query, return JSON
            var orders = await query.ToListAsync();
            return Ok(orders);
        }

        // --------------------------------------------------------------
        // 2) GET: api/orders/5
        //    Returns details for a single order by ID
        // --------------------------------------------------------------
        [HttpGet("{id}")]
        public async Task<ActionResult<Order>> GetOrder(int id)
        {
            var order = await _context.Orders
                                      .Include(o => o.Items)
                                      .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
            {
                return NotFound(); // 404
            }

            return Ok(order); // 200 + Order data in JSON
        }

        // --------------------------------------------------------------
        // 3) POST: api/orders
        //    Creates a new Order. We replicate your "Create" method logic:
        //    - Price calculations for Blanket/Carpet
        //    - Setting default data, etc.
        // --------------------------------------------------------------
        [HttpPost]
        public async Task<ActionResult<Order>> CreateOrder([FromBody] Order order)
        {
            // Validate incoming model
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Enforce address if needed
            if (order.ServiceType == "PickupDelivery" && string.IsNullOrWhiteSpace(order.DeliveryAddress))
            {
                ModelState.AddModelError("DeliveryAddress", "Delivery address is required for pickup/delivery orders.");
                return BadRequest(ModelState);
            }

            // Price logic for each item
            foreach (var item in order.Items)
            {
                if (item.Type == "Blanket")
                {
                    item.Price = 50;
                    item.Length = null;
                    item.Width = null;
                }
                else if (item.Type == "Carpet" && item.Length.HasValue && item.Width.HasValue)
                {
                    var baseRate = (order.ServiceType == "PickupDelivery") ? 17 : 15;
                    item.Price = item.Length.Value * item.Width.Value * baseRate;
                }
            }

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // Return 201 + the newly created resource
            return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order);
        }

        // --------------------------------------------------------------
        // 4) PUT: api/orders/5
        //    Updates an existing Order. We replicate your "Edit" logic:
        //    - Recalculate item prices
        //    - Clear existing items, add new ones
        //    - Set CompletedDate if status is "Ready"
        // --------------------------------------------------------------
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateOrder(int id, [FromBody] Order order)
        {
            if (id != order.Id)
            {
                return BadRequest("ID in URL doesn't match Order.Id.");
            }
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Enforce address if needed
            if (order.ServiceType == "PickupDelivery" && string.IsNullOrWhiteSpace(order.DeliveryAddress))
            {
                ModelState.AddModelError("DeliveryAddress", "Delivery address is required for pickup/delivery orders.");
                return BadRequest(ModelState);
            }

            // Find the existing order in the DB with its Items
            var existingOrder = await _context.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (existingOrder == null)
            {
                return NotFound();
            }

            // Update scalar properties
            existingOrder.CustomerId = order.CustomerId;
            existingOrder.TelephoneNumber = order.TelephoneNumber;
            existingOrder.ReceivedDate = order.ReceivedDate;
            existingOrder.Status = order.Status;
            existingOrder.ServiceType = order.ServiceType;
            existingOrder.DeliveryAddress = order.DeliveryAddress;

            if (order.Status == "Ready" && existingOrder.CompletedDate == null)
            {
                existingOrder.CompletedDate = DateTime.Now;
            }
            else if (order.Status != "Ready")
            {
                existingOrder.CompletedDate = null;
            }

            // ----- Update the Items collection in a granular way -----
            // Identify incoming item IDs (existing items will have non-zero IDs)
            var incomingItemIds = order.Items.Select(i => i.Id).Where(id => id != 0).ToList();

            // Remove items that exist in DB but are not present in the incoming list
            var itemsToRemove = existingOrder.Items
                .Where(i => i.Id != 0 && !incomingItemIds.Contains(i.Id))
                .ToList();
            foreach (var item in itemsToRemove)
            {
                _context.Items.Remove(item);
            }

            // Process each incoming item
            foreach (var incomingItem in order.Items)
            {
                // If the item already exists, update its properties
                var existingItem = existingOrder.Items.FirstOrDefault(i => i.Id == incomingItem.Id);
                if (existingItem != null)
                {
                    existingItem.Type = incomingItem.Type;
                    existingItem.Length = incomingItem.Length;
                    existingItem.Width = incomingItem.Width;

                    // Recalculate price
                    if (existingItem.Type == "Blanket")
                    {
                        existingItem.Price = 50;
                        existingItem.Length = null;
                        existingItem.Width = null;
                    }
                    else if (existingItem.Type == "Carpet" && existingItem.Length.HasValue && existingItem.Width.HasValue)
                    {
                        var baseRate = (existingOrder.ServiceType == "PickupDelivery") ? 17 : 15;
                        existingItem.Price = existingItem.Length.Value * existingItem.Width.Value * baseRate;
                    }
                }
                else
                {
                    // New item: add it to the order
                    var newItem = new Item
                    {
                        Type = incomingItem.Type,
                        Length = incomingItem.Length,
                        Width = incomingItem.Width
                    };

                    // Calculate price for the new item
                    if (newItem.Type == "Blanket")
                    {
                        newItem.Price = 50;
                    }
                    else if (newItem.Type == "Carpet" && newItem.Length.HasValue && newItem.Width.HasValue)
                    {
                        var baseRate = (existingOrder.ServiceType == "PickupDelivery") ? 17 : 15;
                        newItem.Price = newItem.Length.Value * newItem.Width.Value * baseRate;
                    }
                    existingOrder.Items.Add(newItem);
                }
            }
            // --------------------------------------------------------

            await _context.SaveChangesAsync();
            return NoContent();
        }


        // --------------------------------------------------------------
        // 5) DELETE: api/orders/5
        //    Deletes the order from the DB
        // --------------------------------------------------------------
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteOrder(int id)
        {
            var order = await _context.Orders
                                      .Include(o => o.Items)
                                      .FirstOrDefaultAsync(o => o.Id == id);
            if (order == null)
            {
                return NotFound();
            }

            _context.Orders.Remove(order);
            await _context.SaveChangesAsync();

            return NoContent(); // 204
        }

        // --------------------------------------------------------------
        // 6) POST: api/orders/bulk-update
        //    Takes a list of Orders (with IDs + new status?), updates each in DB
        //    This replicates your "BulkUpdate" or "MassUpdateStatus" approach.
        // --------------------------------------------------------------
        [HttpPost("bulk-update")]
        public async Task<IActionResult> BulkUpdateStatus([FromBody] List<Order> orders)
        {
            if (orders == null || orders.Count == 0)
            {
                return BadRequest("No orders provided.");
            }

            foreach (var updatedOrder in orders)
            {
                // Find existing
                var existing = await _context.Orders
                                             .FirstOrDefaultAsync(o => o.Id == updatedOrder.Id);
                if (existing != null)
                {
                    // Update the status
                    existing.Status = updatedOrder.Status;

                    // If status is "Ready," set CompletedDate
                    if (updatedOrder.Status == "Ready")
                    {
                        existing.CompletedDate = DateTime.Now;
                    }
                    else
                    {
                        existing.CompletedDate = null;
                    }
                }
            }

            await _context.SaveChangesAsync();
            return Ok("Bulk update successful.");
        }

        // --------------------------------------------------------------
        // 7) POST: api/orders/{id}/update-status
        //    If you want a single endpoint for updating just the status.
        //    This replicates your "UpdateStatus" from the MVC code.
        // --------------------------------------------------------------
        [HttpPost("{id}/update-status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] string newStatus)
        {
            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);
            if (order == null)
                return NotFound();

            order.Status = newStatus;

            if (newStatus == "Ready")
            {
                order.CompletedDate = DateTime.Now;
            }
            else
            {
                order.CompletedDate = null;
            }

            await _context.SaveChangesAsync();
            return Ok($"Status updated to {newStatus} for Order ID {id}.");
        }
    }
}
