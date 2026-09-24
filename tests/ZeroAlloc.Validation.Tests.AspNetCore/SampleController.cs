using Microsoft.AspNetCore.Mvc;

namespace ZeroAlloc.Validation.Tests.AspNetCore;

[ApiController]
[Route("sample")]
public class SampleController : ControllerBase
{
    [HttpPost]
    public IActionResult Post([FromBody] SampleModel model) =>
        Ok(new { model.Name, model.Quantity });

    [HttpPost("record")]
    public IActionResult PostRecord([FromBody] SampleRecordModel model) =>
        Ok(new { model.Name });

    [HttpPost("unknown")]
    public IActionResult PostUnknown([FromBody] string raw) => Ok(raw);
}
