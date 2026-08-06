# WhatsBiz ERP V1 Release Candidate

Release date: 2026-08-06

V1 consolidates Sprints 0001A–0014. Sprint-0015 freezes features and adds production hardening: SQL health verification, rate limiting, response compression, reverse-proxy handling, production-only HSTS, development-only Swagger, API security headers, secret-free production defaults, corrected Docker Angular output, database foreign-key indexes, API readiness tests, and complete deployment/operations documentation.

Known operational requirement: full database restore is a maintenance operation and must run with the API stopped. The administration restore endpoint performs checksum/header validation; use the deployment runbook for actual recovery.
