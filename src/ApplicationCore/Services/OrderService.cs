using System;
using System.Linq;
using System.Text;
using System.Net.Http;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using BlazorShared;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderService : IOrderService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IRepository<Basket> _basketRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly ILogger _logger;

    private readonly string _orderRecipeUrl;
    private readonly string _serviceBusConnectionString;
    private readonly string _queueName;

    public OrderService(IRepository<Basket> basketRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Order> orderRepository,
        IUriComposer uriComposer,
        ILogger<OrderService> logger,
        IOptions<AzureFunctionsConfiguration> options,
        IOptions<ServiceBusConfiguration> serviceBusOptions)
    {
        _orderRepository = orderRepository;
        _uriComposer = uriComposer;
        _basketRepository = basketRepository;
        _itemRepository = itemRepository;
        _logger = logger;
        _orderRecipeUrl = options.Value.SendOrderRecipe;
        _serviceBusConnectionString = serviceBusOptions.Value.ConnectionString;
        _queueName = serviceBusOptions.Value.QueueName;
    }

    private async Task SendToWarehouse(string orderJson)
    {
        await using (var client = new ServiceBusClient(_serviceBusConnectionString)) 
        {
            
            ServiceBusSender sender = client.CreateSender(_queueName);
            ServiceBusMessage message = new ServiceBusMessage(orderJson);
            try
            {
                // Send the message
                await sender.SendMessageAsync(message);
                _logger.LogInformation("Message was sent successfully.");
            }
            catch (Exception exception)
            {
                _logger.LogInformation($"Exception: {exception.Message}");
            }
            finally
            {
                // Close the sender
                await sender.DisposeAsync();
            }
        }
    }

    public async Task CreateOrderAsync(int basketId, Address shippingAddress)
    {
        var basketSpec = new BasketWithItemsSpecification(basketId);
        var basket = await _basketRepository.FirstOrDefaultAsync(basketSpec);

        Guard.Against.Null(basket, nameof(basket));
        Guard.Against.EmptyBasketOnCheckout(basket.Items);

        var catalogItemsSpecification = new CatalogItemsSpecification(basket.Items.Select(item => item.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification);

        var items = basket.Items.Select(basketItem =>
        {
            var catalogItem = catalogItems.First(c => c.Id == basketItem.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            var orderItem = new OrderItem(itemOrdered, basketItem.UnitPrice, basketItem.Quantity);
            return orderItem;
        }).ToList();

        var order = new Order(basket.BuyerId, shippingAddress, items);

        await _orderRepository.AddAsync(order);

        var httpClient = new HttpClient();
        var json = order.ToJson();
        var data = new StringContent(json, Encoding.UTF8, "application/json");

        await httpClient.PostAsync(_orderRecipeUrl, data);
        await SendToWarehouse(json);
    }
}
