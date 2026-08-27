# KhataDhari marketing-site integration

The public marketing website source is not included in this repository. Deploy `book-demo.js` and `book-demo.css` in its existing asset pipeline and merge `book-demo-form.html` into the existing Book a Demo modal/section. Keep the `#book-demo-form` id and field `name` attributes.

When the website and API share an origin, leave `data-api-base` empty. Otherwise set it to the public API origin and add that exact HTTPS origin to `Cors:AllowedOrigins`. The script reads UTM values, landing URL, and referrer automatically. It obtains the optional WhatsApp number and CAPTCHA public key from the API, so neither is hard-coded in browser code.
