using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WhatsBiz.Infrastructure.Notifications;

namespace WhatsBiz.Api.Controllers;

[ApiController, Authorize, Route("api/config/features")]
public sealed class FeaturesController(IOptionsSnapshot<FeatureOptions> options) : ControllerBase
{
    [HttpGet]
    public ClientFeatureState Get() => new(options.Value.WhatsApp.Enabled, options.Value.Sms.Enabled);
}
