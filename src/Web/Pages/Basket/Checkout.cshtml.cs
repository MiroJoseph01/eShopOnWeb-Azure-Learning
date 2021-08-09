using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Azure.ServiceBus;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.eShopWeb.Web.ViewModels;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Web.Pages.Basket
{
    [Authorize]
    public class CheckoutModel : PageModel
    {
        private readonly IBasketService _basketService;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IOrderService _orderService;
        private string _username = null;
        private readonly IBasketViewModelService _basketViewModelService;
        private readonly IAppLogger<CheckoutModel> _logger;
        private readonly HttpClient _httpClient;
        private const string ServiceBusConnectionString = "Endpoint=sb://service-bus-vb.servicebus.windows.net/;SharedAccessKeyName=order-send;SharedAccessKey=5HM5YAwTMxKP+RSQjSRRyPA1E3sWI1ZRcsfm4bA6Srg=";
        private const string QueueName = "ordermessages";

        public CheckoutModel(IBasketService basketService,
            IBasketViewModelService basketViewModelService,
            SignInManager<ApplicationUser> signInManager,
            IOrderService orderService,
            IAppLogger<CheckoutModel> logger,
            HttpClient httpClient)
        {
            _basketService = basketService;
            _signInManager = signInManager;
            _orderService = orderService;
            _basketViewModelService = basketViewModelService;
            _logger = logger;
            _httpClient = httpClient;
        }

        public BasketViewModel BasketModel { get; set; } = new BasketViewModel();

        public async Task OnGet()
        {
            await SetBasketModelAsync();
        }

        public async Task<IActionResult> OnPost(IEnumerable<BasketItemViewModel> items)
        {
            try
            {
                await SetBasketModelAsync();

                if (!ModelState.IsValid)
                {
                    return BadRequest();
                }

                var updateModel = items.ToDictionary(b => b.Id.ToString(), b => b.Quantity);
                await _basketService.SetQuantities(BasketModel.Id, updateModel);
                var order = await _orderService.CreateOrderAsync(BasketModel.Id, new Address("123 Main St.", "Kent", "OH", "United States", "44240"));
                await _basketService.DeleteBasketAsync(BasketModel.Id);

                await SendOrderMessageAsync(JsonConvert.SerializeObject(BasketModel.Items));

                var orderJson = JsonConvert.SerializeObject(new
                {
                    ShippingAddress = new
                    {
                        Street = "123 Main St.",
                        City = "Kent",
                        State = "OH",
                        Country = "United States",
                        ZipCode = "44240",
                    },
                    Items = order.OrderItems.Select(i => new
                    {
                        i.Id,
                        i.ItemOrdered.CatalogItemId,
                        i.ItemOrdered.ProductName,
                        i.UnitPrice,
                        i.Units,
                    }),
                    FinalPrice = order.Total()
                });

                await _httpClient.PostAsync(
                   "https://deliveryorderprocessortocosmosdb.azurewebsites.net/api/DeliveryOrderProcessor?code=bYwNqgfG/JMhLkssZcQ8KV9PPu4odBLGQLtrvaRxorZ63uZSa42/og==",
                   new StringContent(orderJson));
            }
            catch (EmptyBasketOnCheckoutException emptyBasketOnCheckoutException)
            {
                //Redirect to Empty Basket page
                _logger.LogWarning(emptyBasketOnCheckoutException.Message);
                return RedirectToPage("/Basket/Index");
            }

            return RedirectToPage("Success");
        }

        static async Task SendOrderMessageAsync(string message)
        {
            var queueClient = new QueueClient(ServiceBusConnectionString, QueueName);

            var busMessage = new Message(Encoding.UTF8.GetBytes(message));
            await queueClient.SendAsync(busMessage);

            await queueClient.CloseAsync();
        }

        private async Task SetBasketModelAsync()
        {
            if (_signInManager.IsSignedIn(HttpContext.User))
            {
                BasketModel = await _basketViewModelService.GetOrCreateBasketForUser(User.Identity.Name);
            }
            else
            {
                GetOrSetBasketCookieAndUserName();
                BasketModel = await _basketViewModelService.GetOrCreateBasketForUser(_username);
            }
        }

        private void GetOrSetBasketCookieAndUserName()
        {
            if (Request.Cookies.ContainsKey(Constants.BASKET_COOKIENAME))
            {
                _username = Request.Cookies[Constants.BASKET_COOKIENAME];
            }
            if (_username != null) return;

            _username = Guid.NewGuid().ToString();
            var cookieOptions = new CookieOptions();
            cookieOptions.Expires = DateTime.Today.AddYears(10);
            Response.Cookies.Append(Constants.BASKET_COOKIENAME, _username, cookieOptions);
        }
    }
}
