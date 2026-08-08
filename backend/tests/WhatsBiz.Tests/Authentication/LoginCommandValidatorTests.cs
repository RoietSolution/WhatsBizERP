using FluentAssertions;
using WhatsBiz.Application.Features.Authentication.Login;
namespace WhatsBiz.Tests.Authentication;
public sealed class LoginCommandValidatorTests { [Fact] public async Task ValidateEmptyCredentialsReturnsErrors() { var result = await new LoginCommandValidator().ValidateAsync(new LoginCommand(string.Empty, string.Empty)); result.IsValid.Should().BeFalse(); result.Errors.Should().HaveCount(2); } }
