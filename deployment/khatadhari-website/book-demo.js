(function ($, window, document) {
  'use strict';
  var $form;
  var captchaWidgetId;
  var publicConfig = {};

  function apiUrl(path) {
    var base = String($form.data('api-base') || '').replace(/\/$/, '');
    return base + path;
  }
  function value(name) { return String($form.find('[name="' + name + '"]').val() || '').trim() || null; }
  function query(name) { return new URLSearchParams(window.location.search).get(name); }
  function digits(valueToCheck) { return valueToCheck.replace(/\D/g, ''); }
  function showError(message) { $form.find('.kd-demo-error').text(message).prop('hidden', false); }
  function setBusy(busy) {
    $form.find('.kd-demo-submit').prop('disabled', busy).attr('aria-busy', busy ? 'true' : 'false');
    $form.find('.kd-submit-label').text(busy ? 'Submitting…' : 'Book My Demo');
  }
  function captchaToken() {
    if (captchaWidgetId === undefined || !window.turnstile) return null;
    return window.turnstile.getResponse(captchaWidgetId) || null;
  }
  function payload() {
    return {
      name: value('name'), mobile: value('mobile'), email: value('email'), businessName: value('businessName'),
      city: value('city'), businessType: value('businessType'), message: value('message'),
      utmSource: query('utm_source'), utmMedium: query('utm_medium'), utmCampaign: query('utm_campaign'),
      utmContent: query('utm_content'), landingPage: window.location.href, referrer: document.referrer || null,
      captchaToken: captchaToken()
    };
  }
  function validate(data) {
    if (!data.name) return 'Please enter your name.';
    if (!data.mobile || digits(data.mobile).length < 10 || digits(data.mobile).length > 15) return 'Please enter a valid mobile number.';
    if (data.email && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(data.email)) return 'Please enter a valid email address.';
    if (publicConfig.captchaEnabled && !data.captchaToken) return 'Please complete the security check.';
    return null;
  }
  function configureCaptcha(config) {
    if (!config.captchaEnabled || !config.captchaSiteKey) return;
    var script = document.createElement('script');
    script.src = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit';
    script.async = true;
    script.defer = true;
    script.onload = function () { captchaWidgetId = window.turnstile.render('.kd-captcha', { sitekey: config.captchaSiteKey }); };
    document.head.appendChild(script);
  }
  function configurePublicContact() {
    $.getJSON(apiUrl('/api/demo-requests/configuration')).done(function (config) {
      publicConfig = config || {};
      if (config.whatsAppContactNumber) {
        var number = digits(config.whatsAppContactNumber);
        $('.kd-whatsapp').attr('href', 'https://wa.me/' + encodeURIComponent(number)).prop('hidden', false);
      }
      configureCaptcha(config);
    });
  }
  function problemMessage(xhr) {
    if (xhr.status === 429) return 'Too many requests were received. Please wait a few minutes and try again.';
    var body = xhr.responseJSON || {};
    return body.detail || body.title || 'We could not submit your request. Please try again.';
  }
  $(function () {
    $form = $('#book-demo-form');
    if (!$form.length) return;
    configurePublicContact();
    $form.on('submit', function (event) {
      event.preventDefault();
      if ($form.find('.kd-demo-submit').prop('disabled')) return;
      $form.find('.kd-demo-error').prop('hidden', true).empty();
      var data = payload();
      var error = validate(data);
      if (error) { showError(error); return; }
      setBusy(true);
      $.ajax({ url: apiUrl('/api/demo-requests'), method: 'POST', contentType: 'application/json; charset=utf-8', dataType: 'json', data: JSON.stringify(data) })
        .done(function (result) {
          $('.kd-customer-name').text(data.name);
          $('.kd-reference').text(result.referenceNo);
          $form.prop('hidden', true);
          $('.kd-demo-success').prop('hidden', false).attr('tabindex', '-1').trigger('focus');
        })
        .fail(function (xhr) {
          showError(problemMessage(xhr));
          if (window.turnstile && captchaWidgetId !== undefined) window.turnstile.reset(captchaWidgetId);
        })
        .always(function () { setBusy(false); });
    });
  });
})(window.jQuery, window, document);
