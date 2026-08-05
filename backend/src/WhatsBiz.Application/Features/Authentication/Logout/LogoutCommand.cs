using MediatR;
namespace WhatsBiz.Application.Features.Authentication.Logout;
public sealed record LogoutCommand(string Token) : IRequest;
