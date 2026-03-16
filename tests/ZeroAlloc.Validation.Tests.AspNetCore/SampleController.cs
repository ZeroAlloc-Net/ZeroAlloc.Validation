using Microsoft.AspNetCore.Mvc;

namespace ZValidation.Tests.AspNetCore;

[ApiController]
[Route("sample")]
public class SampleController : ControllerBase
{
    [HttpPost]
    public IActionResult Post([FromBody] SampleModel model) =>
        Ok(new { model.Name, model.Quantity });

    [HttpPost("unknown")]
    public IActionResult PostUnknown([FromBody] string raw) => Ok(raw);
}
