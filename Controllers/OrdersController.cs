using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Vonage.Messaging;
using Vonage.Request;
using Vonage;
using ApiSpalatorie.Models;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using ApiSpalatorie.Data;


namespace AplicatieSpalatorie.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly VonageSettings _vonage;
        private readonly GoogleMapsSettings _maps;

        public OrdersController(ApplicationDbContext context, IOptions<VonageSettings> vonageOptions, IOptions<GoogleMapsSettings> maps)
        {
            _context = context;
            _vonage = vonageOptions.Value;
            _maps = maps.Value;
        }



        // GET: api/orders/service-types
        [HttpGet("service-types")]
        [AllowAnonymous]
        public IActionResult GetServiceTypes()
            => Ok(new[] { "Office", "PickupDelivery" });

        // GET: api/orders/statuses
        [HttpGet("statuses")]
        [AllowAnonymous]
        public IActionResult GetStatuses()
            => Ok(new[] { "Pending", "In Progress", "Ready" });




        private async Task<(double lat, double lng)> GeocodeAddressAsync(string address)
        {
            var url = $"https://maps.googleapis.com/maps/api/geocode/json?address={Uri.EscapeDataString(address)}&key={_maps.ApiKey}";

            using var client = new HttpClient();
            var res = await client.GetFromJsonAsync<GeocodeResponse>(url);

            var location = res?.results?.FirstOrDefault()?.geometry?.location;

            if (location == null)
                throw new InvalidOperationException("No location found from geocoding response.");

            return (location?.lat ?? 0, location?.lng ?? 0);
        }




        // Allow only customers
        // GET api/orders/my/{id}
        [Authorize(Roles = "Customer")]
        [HttpGet("my/{id}")]
        public async Task<IActionResult> GetMyOrder(int id)
        {
            var phone = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(phone)) return Unauthorized();

            // load the order + items
            var order = await _context.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == id && o.TelephoneNumber == phone);
            if (order == null) return NotFound();

            // try to find a DeliveryRouteOrder entry
            var routeOrder = await _context.DeliveryRouteOrders
                .Include(ro => ro.DeliveryRoute)
                .FirstOrDefaultAsync(ro => ro.OrderId == id);

            return Ok(new
            {
                // all your existing order fields...
                order.Id,
                order.Status,
                order.ReceivedDate,
                order.DeliveryAddress,
                order.Items,
                order.CompletedDate,

                // **new**:
                routeId = routeOrder?.DeliveryRouteId,
                isRouteStarted = routeOrder?.DeliveryRoute.IsStarted ?? false
            });
        }

        // GET api/orders/my
        [Authorize(Roles = "Customer")]
        [HttpGet("my")]
        public async Task<IActionResult> GetMyOrders()
        {
            var phone = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(phone)) return Unauthorized();

            var orders = await _context.Orders
                .Where(o => o.TelephoneNumber == phone)
                .Include(o => o.Items)
                .ToListAsync();

            // fetch links & routes in bulk
            var links = await _context.DeliveryRouteOrders
                .Where(ro => orders.Select(o => o.Id).Contains(ro.OrderId))
                .ToListAsync();

            var routeIds = links.Select(l => l.DeliveryRouteId).Distinct().ToList();
            var routes = await _context.DeliveryRoutes
                .Where(r => routeIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.IsStarted);

            var result = orders.Select(o => {
                var link = links.FirstOrDefault(ro => ro.OrderId == o.Id);
                var rid = link?.DeliveryRouteId;
                return new
                {
                    o.Id,
                    o.Status,
                    o.ReceivedDate,
                    o.DeliveryAddress,
                    o.DeliveryLatitude,
                    o.DeliveryLongitude,
                    Items = o.Items,
                    RouteId = rid,
                    RouteStarted = rid.HasValue && routes.TryGetValue(rid.Value, out var started) && started
                };
            });

            return Ok(result);
        }





        //returns orders, can be filtered by search, date, or status.
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

            // we execute the query and then we return a json
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
        [Authorize(Roles = "Admin,Manager,Customer")]
        [HttpPost]
        public async Task<ActionResult<Order>> CreateOrder([FromBody] Order order)
        {
            // If a Customer is creating the order, pull phone & name out of their JWT
            if (User.IsInRole("Customer"))
            {
                // NameIdentifier was set to the phone number in OtpController.Verify :contentReference[oaicite:0]{index=0}
                var phone = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
                // Name was set to the customer’s real name in OtpController.Verify :contentReference[oaicite:1]{index=1}
                var name = User.FindFirstValue(ClaimTypes.Name)!;

                order.TelephoneNumber = phone;
                order.CustomerId = name;    // now stores their real name
            }

            // Validate model
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            // Ensure delivery address when needed
            if (order.ServiceType == "PickupDelivery"
             && string.IsNullOrWhiteSpace(order.DeliveryAddress))
            {
                ModelState.AddModelError(
                  nameof(order.DeliveryAddress),
                  "Delivery address is required for pickup/delivery orders.");
                return BadRequest(ModelState);
            }


            if (order.ServiceType == "PickupDelivery" && !string.IsNullOrWhiteSpace(order.DeliveryAddress))
            {
                var (lat, lng) = await GeocodeAddressAsync(order.DeliveryAddress);
                order.DeliveryLatitude = lat;
                order.DeliveryLongitude = lng;
            }


            // Price‐calculate each item
            foreach (var item in order.Items)
            {
                if (item.Type == "Blanket")
                {
                    item.Price = 50;
                    item.Length = null;
                    item.Width = null;
                }
                else if (item.Type == "Carpet"
                      && item.Length.HasValue
                      && item.Width.HasValue)
                {
                    var rate = order.ServiceType == "PickupDelivery" ? 17 : 15;
                    item.Price = item.Length.Value * item.Width.Value * rate;
                }
            }

            // Save to DB
            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // Send confirmation SMS
            var msg = $"Your order #{order.Id} was created! We'll be in touch soon.";
            await SendSmsAsync(order.TelephoneNumber, msg);

            // Return 201 with the new order
            return CreatedAtAction(
              nameof(GetOrder),
              new { id = order.Id },
              order
            );
        }


        // This method updates an existing order. We can add or remove items, change statuses etc
        [Authorize(Roles = "Admin,Manager")]
        [HttpPut("{id}")]
      
        public async Task<IActionResult> UpdateOrder(int id, [FromBody] Order order)
        {
            if (id != order.Id)
                return BadRequest("ID in URL doesn't match Order.Id.");

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            // Pickup-delivery requires address
            if (order.ServiceType == "PickupDelivery" && string.IsNullOrWhiteSpace(order.DeliveryAddress))
            {
                ModelState.AddModelError("DeliveryAddress", "Delivery address is required for pickup/delivery orders.");
                return BadRequest(ModelState);
            }

            // Load existing order
            var existing = await _context.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (existing == null)
                return NotFound();

            /* -----------------------------------------------------------
             * Remember the old status so we can detect a change later
             * ----------------------------------------------------------*/
            var previousStatus = existing.Status;

            /* ----------  Update scalar fields  ---------- */
            existing.CustomerId = order.CustomerId;
            existing.TelephoneNumber = order.TelephoneNumber;
            existing.ReceivedDate = order.ReceivedDate;
            existing.Status = order.Status;
            existing.ServiceType = order.ServiceType;
            existing.DeliveryAddress = order.DeliveryAddress;
            existing.Observation = order.Observation;

            /* ----------  CompletedDate logic  ---------- */
            if (existing.Status == "Ready" && existing.CompletedDate == null)
                existing.CompletedDate = DateTime.Now;
            else if (existing.Status != "Ready")
                existing.CompletedDate = null;

            /* ----------  Sync items collection  ---------- */
            var incomingIds = order.Items.Where(i => i.Id != 0).Select(i => i.Id).ToList();

            // remove deleted items
            var toRemove = existing.Items
                                   .Where(i => i.Id != 0 && !incomingIds.Contains(i.Id))
                                   .ToList();
            foreach (var item in toRemove)
                _context.Items.Remove(item);

            foreach (var inc in order.Items)
            {
                var cur = existing.Items.FirstOrDefault(i => i.Id == inc.Id);
                if (cur != null)
                {
                    // update existing item
                    cur.Type = inc.Type;
                    cur.Length = inc.Length;
                    cur.Width = inc.Width;
                }
                else
                {
                    // add new item
                    cur = new Item
                    {
                        Type = inc.Type,
                        Length = inc.Length,
                        Width = inc.Width
                    };
                    existing.Items.Add(cur);
                }

                /* price calculation */
                if (cur.Type == "Blanket")
                {
                    cur.Price = 50;
                    cur.Length = cur.Width = null;
                }
                else if (cur.Type == "Carpet" && cur.Length.HasValue && cur.Width.HasValue)
                {
                    var rate = existing.ServiceType == "PickupDelivery" ? 17 : 15;
                    cur.Price = cur.Length.Value * cur.Width.Value * rate;
                }
            }

            /* ----------  Save first, then SMS  ---------- */
            await _context.SaveChangesAsync();

            /* ----------  SMS on status change  ---------- */
            if (previousStatus != existing.Status)
            {
                string? sms = existing.Status switch
                {
                    "In Progress" => $"Order #{existing.Id} is now in progress.",
                    "Ready" => $"Good news! Order #{existing.Id} is ready for pickup.",
                    _ => null
                };

                if (sms != null)
                    await SendSmsAsync(existing.TelephoneNumber, sms);
            }

            return NoContent();
        }



        private async Task SendSmsAsync(string toPhone, string text)
        {
            // Ensure E.164 format for Romania if no '+' present
            var normalized = toPhone.StartsWith("+")
               ? toPhone
               : $"+40{toPhone}";

            var creds = Credentials.FromApiKeyAndSecret(_vonage.ApiKey, _vonage.ApiSecret);
            var client = new VonageClient(creds);

            var response = await client.SmsClient.SendAnSmsAsync(new SendSmsRequest
            {
                To = normalized,
                From = _vonage.From,
                Text = text
            });

            var msg = response.Messages.FirstOrDefault();
            if (msg == null || msg.Status != "0")
                throw new InvalidOperationException($"SMS failed to {normalized}: {msg?.ErrorText}");

            
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
                return BadRequest("No orders provided.");

            foreach (var updatedOrder in orders)
            {
                var existing = await _context.Orders
                                             .FirstOrDefaultAsync(o => o.Id == updatedOrder.Id);
                if (existing != null)
                {
                    existing.Status = updatedOrder.Status;
                    existing.CompletedDate = updatedOrder.Status == "Ready"
                        ? DateTime.Now
                        : (DateTime?)null;

                    // Send SMS for key status changes
                    var message = updatedOrder.Status switch
                    {
                        "In Progress" => $"Order #{existing.Id} is now in progress.",
                        "Ready" => $"Good news! Order #{existing.Id} is ready for pickup.",
                        _ => null
                    };
                    if (message != null)
                        await SendSmsAsync(existing.TelephoneNumber, message);
                }
            }

            await _context.SaveChangesAsync();
            return Ok("Bulk update successful.");
        }

        // Single‐order status update (Admin, Manager only)
        [Authorize(Roles = "Admin,Manager")]
        [HttpPost("{id}/update-status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] string newStatus)
        {
            var existing = await _context.Orders
                                         .FirstOrDefaultAsync(o => o.Id == id);
            if (existing == null)
                return NotFound();

            existing.Status = newStatus;
            existing.CompletedDate = newStatus == "Ready" ? DateTime.Now : (DateTime?)null;

            await _context.SaveChangesAsync();

            // SMS notification
            var message = newStatus switch
            {
                "In Progress" => $"Order #{existing.Id} is now in progress.",
                "Ready" => $"Good news! Order #{existing.Id} is ready for pickup.",
                _ => null
            };
            if (message != null)
                await SendSmsAsync(existing.TelephoneNumber, message);

            return Ok($"Status updated to {newStatus} for Order ID {id}.");
        }
    }
}
