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

    [HttpPost("nested/orders")]
    public IActionResult PostNestedOrder([FromBody] Orders.Request model) => Ok(new { model.Name });

    [HttpPost("nested/returns")]
    public IActionResult PostNestedReturn([FromBody] Returns.Inbound.Request model) =>
        Ok(new { model.Quantity });

    [HttpPost("composed")]
    public IActionResult PostComposed([FromBody] Shipment model) => Ok(new { model.Parcel.Weight });

    [HttpPost("delivery")]
    public IActionResult PostDelivery([FromBody] Delivery model) => Ok(new { model.Crate.Label });

    [HttpPost("signup")]
    public IActionResult PostSignup([FromBody] Signup model) => Ok(new { model.UserName });

    // Generic closings as action arguments, dispatched through the IModelValidator registry, #238.
    [HttpPost("crate/parcel")]
    public IActionResult PostParcelCrate([FromBody] Crate<Parcel> model) => Ok(new { model.Label });

    [HttpPost("crate/sample")]
    public IActionResult PostSampleCrate([FromBody] Crate<SampleModel> model) => Ok(new { model.Label });

    [HttpPost("crate/special")]
    public IActionResult PostSpecialCrate([FromBody] SpecialCrate model) => Ok(new { model.Label });

    [HttpPost("crate/signup")]
    public IActionResult PostSignupCrate([FromBody] Crate<Signup> model) => Ok(new { model.Label });

    [HttpPost("unknown")]
    public IActionResult PostUnknown([FromBody] string raw) => Ok(raw);
}
