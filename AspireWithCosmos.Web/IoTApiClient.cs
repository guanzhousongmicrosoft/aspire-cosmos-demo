using Refit;

namespace AspireWithCosmos.Web;

public interface IIoTApiClient
{
    [Post("/iots")]
    Task<IoTDevice> Create(IoTDevice device);

    [Get("/iots")]
    Task<List<IoTDevice>> Retrieve();

    [Put("/iots/{id}")]
    Task<IoTDevice> Update(string id, IoTDevice device);

    [Delete("/iots/{userId}/{id}")]
    Task Delete(string userId, string id);
}

public class IoTApiClient(HttpClient httpClient) : IIoTApiClient
{
    private readonly HttpClient _httpClient = httpClient;

    public async Task<IoTDevice> Create(IoTDevice device)
        => await RestService.For<IIoTApiClient>(_httpClient).Create(device);

    public async Task<List<IoTDevice>> Retrieve()
        => await RestService.For<IIoTApiClient>(_httpClient).Retrieve();

    public async Task<IoTDevice> Update(string id, IoTDevice device)
        => await RestService.For<IIoTApiClient>(_httpClient).Update(id, device);

    public async Task Delete(string userId, string id)
        => await RestService.For<IIoTApiClient>(_httpClient).Delete(userId, id);
}

// The IoT service model used for transmitting data
public record IoTDevice(string Description, string UserId)
{
    public required string id { get; set; }
    public bool IsOnline { get; set; }
}
