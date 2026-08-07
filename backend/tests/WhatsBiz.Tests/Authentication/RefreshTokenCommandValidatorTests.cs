using FluentAssertions;
using WhatsBiz.Application.Features.Authentication.RefreshToken;
namespace WhatsBiz.Tests.Authentication;
public sealed class RefreshTokenCommandValidatorTests { [Fact] public async Task ValidateEmptyTokenReturnsError() { var result = await new RefreshTokenCommandValidator().ValidateAsync(new RefreshTokenCommand(string.Empty)); result.IsValid.Should().BeFalse(); } }
