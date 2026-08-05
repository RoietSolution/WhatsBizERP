using FluentValidation;
namespace WhatsBiz.Application.Features.Authentication.Logout;
public sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand> { public LogoutCommandValidator() { RuleFor(x => x.Token).NotEmpty().MaximumLength(512); } }
