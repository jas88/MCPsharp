using System;
using System.Threading.Tasks;
using System.Net.Http;

class DataService
{
    private HttpClient _client = new HttpClient();

    public async Task ProcessAsync()
    {
        var data = "test";
        var response = await _client.GetAsync("http://example.com");
        var content = await response.Content.ReadAsStringAsync();
        Console.WriteLine(content);
    }
}