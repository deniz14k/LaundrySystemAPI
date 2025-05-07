using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AplicatieSpalatorie.Data;
using AplicatieSpalatorie.Models;
using Microsoft.AspNetCore.Authorization;

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


        // returns orders, can be filtered by search, date, or status.
        [Authorize(Roles = "Admin,Clerk,Manager")]
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Order>>> GetOrders(
            [FromQuery] string? searchTerm,
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate,
            [FromQuery] string? status)
        {
            IQueryable<Order> query = _context.Orders
                                              .Include(o => o.Items);

            // if we have filters applied
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

            // we execute the query and then return a json
            var orders = await query.ToListAsync();
            return Ok(orders);
        }

        // this get method returns an order by a given id
        [Authorize(Roles = "Admin,Clerk,Manager")]
        [HttpGet("{id}")]
        public async Task<ActionResult<Order>> GetOrder(int id)
        {
            var order = await _context.Orders
                                      .Include(o => o.Items)
                                      .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
            {
                return NotFound(); 
            }

            return Ok(order); 
        }

        // method for creating a new order.
        [Authorize(Roles = "Admin,Manager")]
        [HttpPost]
        public async Task<ActionResult<Order>> CreateOrder([FromBody] Order order)
        {
            // validation
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // address needed if pickup delivery order
            if (order.ServiceType == "PickupDelivery" && string.IsNullOrWhiteSpace(order.DeliveryAddress))
            {
                ModelState.AddModelError("DeliveryAddress", "Delivery address is required for pickup/delivery orders.");
                return BadRequest(ModelState);
            }

            // prices for items
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

            return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order);
        }


        // This method updates an existing order. We can add or remove items, change statuses etc
        [Authorize(Roles = "Admin,Manager")]
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

            // if we need address(only for pickup delivery orders)
            if (order.ServiceType == "PickupDelivery" && string.IsNullOrWhiteSpace(order.DeliveryAddress))
            {
                ModelState.AddModelError("DeliveryAddress", "Delivery address is required for pickup/delivery orders.");
                return BadRequest(ModelState);
            }

            // find the existing order in database
            var existingOrder = await _context.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (existingOrder == null)
            {
                return NotFound();
            }

            // update properties
            existingOrder.CustomerId = order.CustomerId;
            existingOrder.TelephoneNumber = order.TelephoneNumber;
            existingOrder.ReceivedDate = order.ReceivedDate;
            existingOrder.Status = order.Status;
            existingOrder.ServiceType = order.ServiceType;
            existingOrder.DeliveryAddress = order.DeliveryAddress;
            existingOrder.Observation = order.Observation;


            if (order.Status == "Ready" && existingOrder.CompletedDate == null)
            {
                existingOrder.CompletedDate = DateTime.Now;
            }
            else if (order.Status != "Ready")
            {
                existingOrder.CompletedDate = null;
            }

            // we update the items
            
            var incomingItemIds = order.Items.Select(i => i.Id).Where(id => id != 0).ToList();


            // we remove items that are not in the incoming list and have an ID != 0
            // (meaning they are already in the database)
            var itemsToRemove = existingOrder.Items
                .Where(i => i.Id != 0 && !incomingItemIds.Contains(i.Id))
                .ToList();
            foreach (var item in itemsToRemove)
            {
                _context.Items.Remove(item);
            }

            // process each item
            foreach (var incomingItem in order.Items)
            {
                // if the item has an ID, we update it
                var existingItem = existingOrder.Items.FirstOrDefault(i => i.Id == incomingItem.Id);
                if (existingItem != null)
                {
                    existingItem.Type = incomingItem.Type;
                    existingItem.Length = incomingItem.Length;
                    existingItem.Width = incomingItem.Width;

                    // price recalculation
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
                    // new item
                    var newItem = new Item
                    {
                        Type = incomingItem.Type,
                        Length = incomingItem.Length,
                        Width = incomingItem.Width
                    };

                    // new item price calculation
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
           

            await _context.SaveChangesAsync();
            return NoContent();
        }


        // delete an order by id
        [Authorize(Roles = "Admin")]
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

            return NoContent(); //204
        }



        // This method is for updating the status of multiple orders with one button and some checkboxes.
        [Authorize(Roles = "Admin,Manager")]
        [HttpPost("bulk-update")]
        public async Task<IActionResult> BulkUpdateStatus([FromBody] List<Order> orders)
        {
            if (orders == null || orders.Count == 0)
            {
                return BadRequest("No orders provided.");
            }

            foreach (var updatedOrder in orders)
            {
                // find the existing order in the database
                var existing = await _context.Orders
                                             .FirstOrDefaultAsync(o => o.Id == updatedOrder.Id);
                if (existing != null)
                {
                    // update the status
                    existing.Status = updatedOrder.Status;

                    // if status is ready,  set the completed date
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

        //this method is for updating the status of a single order
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
