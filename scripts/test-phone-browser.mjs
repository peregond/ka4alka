// Uses the actual embedded LAN page and HTTP server; data is an explicit test fixture.
import { createRequire } from "node:module";
import { spawn } from "node:child_process";
import { createInterface } from "node:readline";
import { mkdir } from "node:fs/promises";
import assert from "node:assert/strict";
const require = createRequire(import.meta.url);
const { chromium, webkit, devices } = require("playwright");
const { expect } = require("playwright/test");
const output = process.env.PHONE_SCREENSHOTS || "test-output/phone-browser";
await mkdir(output, { recursive: true });
for (const [name, engine] of [
  ["chromium", chromium],
  ["webkit", webkit],
]) {
  if (process.env.PHONE_BROWSER && process.env.PHONE_BROWSER !== name) continue;
  const browser = await engine.launch({
    ...(name === "chromium" && process.env.CHROMIUM_PATH
      ? { executablePath: process.env.CHROMIUM_PATH }
      : name === "webkit" && process.env.WEBKIT_PATH
        ? { executablePath: process.env.WEBKIT_PATH }
        : {}),
    args: name === "chromium" ? ["--no-sandbox"] : [],
  });
  try {
    for (const locale of ["ru-RU", "en-US"])
      for (const blockedStorage of [false, true]) {
        const fixture = spawn(
          process.env.DOTNET_COMMAND || "dotnet",
          [
            "lan-tests/bin/Release/net10.0/Kachalka.LanTests.dll",
            "--phone-browser",
          ],
          { stdio: ["pipe", "pipe", "inherit"] },
        );
        const config = await new Promise((resolve, reject) => {
          const input = createInterface({ input: fixture.stdout });
          input.once("line", (line) => {
            input.close();
            resolve(JSON.parse(line));
          });
          fixture.once("exit", (code) =>
            reject(Error("Phone fixture exited: " + code)),
          );
        });
        const context = await browser.newContext({
          ...devices["iPhone 13"],
          viewport: { width: 390, height: 844 },
          locale,
          colorScheme: blockedStorage ? "dark" : "light",
        });
        if (blockedStorage)
          await context.addInitScript(() => {
            Object.defineProperty(window, "sessionStorage", {
              get() {
                throw new DOMException("Storage blocked", "SecurityError");
              },
            });
          });
        try {
          const page = await context.newPage(),
            errors = [],
            policyViolations = [],
            policyMessages = [],
            commands = [],
            catalogRequests = [],
            adds = [];
          // WebKit's screenshot helper injects an inline `body {}` stylesheet.
          // Keep the production CSP and all functional/CSP assertions intact;
          // collect visual artifacts in Chromium without that injected stylesheet.
          const capture = (options) =>
            name === "chromium" ? page.screenshot(options) : Promise.resolve();
          await page.addInitScript(() => {
            document.addEventListener("securitypolicyviolation", (event) => {
              console.log(
                "PHONE_POLICY " +
                  JSON.stringify({
                    directive: event.effectiveDirective,
                    blocked: event.blockedURI,
                    sample: event.sample,
                  }),
              );
            });
          });
          // Diagnostic reporting only in the test response: retain all enforcing
          // directives and add report-sample so UA styles can be identified.
          // The shipped server header remains unchanged.
          await page.route("http://127.0.0.1:*/", async (route) => {
            const response = await route.fetch();
            const headers = response.headers();
            assert(
              headers["content-security-policy"].includes("style-src 'self';"),
            );
            headers["content-security-policy"] = headers[
              "content-security-policy"
            ].replace("style-src 'self';", "style-src 'self' 'report-sample';");
            await route.fulfill({ response, headers });
          });
          let confirmAction = true;
          page.on("pageerror", (error) => errors.push(error.message));
          page.on("console", (message) => {
            if (message.text().startsWith("PHONE_POLICY "))
              policyViolations.push(JSON.parse(message.text().slice(13)));
            if (
              message.type() === "error" &&
              /content security policy|refused to/i.test(message.text())
            )
              policyMessages.push(message.text());
          });
          page.on("dialog", (dialog) =>
            confirmAction ? dialog.accept() : dialog.dismiss(),
          );
          page.on("request", (request) => {
            const path = new URL(request.url()).pathname;
            if (path === "/api/control") commands.push(request.postDataJSON());
            if (path === "/api/catalog")
              catalogRequests.push(request.postDataJSON());
            if (path === "/api/add") adds.push(request.postDataJSON());
          });
          await page.goto(
            `http://127.0.0.1:${config.port}/${blockedStorage ? "#pair=" + config.code : ""}`,
          );
          assert.equal(
            await page.locator("#pair h2").textContent(),
            locale.startsWith("ru")
              ? "Подключить телефон"
              : "Connect your phone",
          );
          if (!blockedStorage) {
            await capture({
              path: `${output}/phone-pair-${name}-${locale}.png`,
              fullPage: true,
            });
            await page.locator("#code").fill(config.code);
            await page.locator("#pair-form button").tap();
          }
          await expect(page.locator(".card")).toHaveCount(8);
          assert(
            !new URL(page.url()).hash,
            "one-time QR credential remains in browser history",
          );
          assert(await page.locator("#pair").isHidden());
          assert.equal(
            await page.locator(".card [onerror]").count(),
            0,
            "source title became executable HTML",
          );
          await expect(page.locator(".card").nth(1)).toContainText("<img");
          await page.locator("#more").tap();
          await expect(page.locator(".card")).toHaveCount(9);
          assert.equal(catalogRequests.at(-1).page, 2);
          assert(await page.locator("#more").isHidden());
          for (const section of ["series", "games", "movies"]) {
            await page.locator('[data-section="' + section + '"]').tap();
            await expect(page.locator(".card")).toHaveCount(8);
            assert.equal(catalogRequests.at(-1).section, section);
          }
          await page.locator("#query").fill("nothing");
          await page.locator("#search button[type=submit]").tap();
          await expect(page.locator("#results .empty-state")).toBeVisible();
          assert.equal(await page.locator(".card").count(), 0);
          await page.locator("#query").fill("fixture");
          await page.locator("#search button[type=submit]").tap();
          await expect(page.locator(".card")).toHaveCount(8);
          if (!blockedStorage) {
            await page.evaluate(() => scrollTo(0, 0));
            await capture({
              path: `${output}/phone-catalog-${name}-${locale}.png`,
              fullPage: true,
            });
          }
          await page.locator(".card").nth(6).scrollIntoViewIfNeeded();
          const returnY = await page.evaluate(() => scrollY),
            requestCount = catalogRequests.length;
          await page.locator(".card").nth(6).tap();
          await expect(page.locator(".row")).toHaveCount(3);
          await expect(page.locator(".row.recommended")).toHaveCount(1);
          await page.locator("#release-quality").selectOption("4K");
          await expect(page.locator(".row")).toHaveCount(1);
          await page.locator("#release-audio").selectOption("Dub");
          await expect(page.locator("#results .empty-state")).toBeVisible();
          await page.locator("#release-quality").selectOption("");
          await page.locator("#release-audio").selectOption("");
          await page.locator("#release-order").selectOption("seeds");
          await expect(page.locator(".row").first()).toContainText("4K");
          assert(
            await page
              .locator(".row")
              .filter({ hasText: "Unavailable fixture" })
              .locator("button")
              .isDisabled(),
          );
          await page.locator("#back").tap();
          await expect(page.locator(".card")).toHaveCount(8);
          await expect(page.locator("#query")).toHaveValue("fixture");
          assert.equal(
            catalogRequests.length,
            requestCount,
            "Back refetched or lost the existing catalog",
          );
          await expect
            .poll(() => page.evaluate(() => scrollY))
            .toBeGreaterThanOrEqual(Math.max(0, returnY - 5));
          await page.locator(".card").first().tap();
          await expect(page.locator(".row")).toHaveCount(3);
          if (!blockedStorage)
            await capture({
              path: `${output}/phone-releases-${name}-${locale}.png`,
              fullPage: true,
            });
          await page.locator(".row.recommended button").tap();
          const activeRow = page.locator('[data-download-id="download"]');
          await expect(activeRow.locator("progress")).toHaveAttribute(
            "value",
            "42",
          );
          await expect(activeRow).toContainText("5");
          await page.locator('[data-download-filter="complete"]').tap();
          await expect(page.locator("[data-download-id]")).toHaveCount(1);
          await expect(
            page.locator('[data-download-id="complete"]'),
          ).toBeVisible();
          await page.locator('[data-download-filter="all"]').tap();
          await expect(page.locator("[data-download-id]")).toHaveCount(3);
          await activeRow.locator('[data-action="pause"]').tap();
          await expect(
            activeRow.locator('[data-action="resume"]'),
          ).toBeVisible();
          await activeRow.locator('[data-action="resume"]').tap();
          await expect(
            activeRow.locator('[data-action="pause"]'),
          ).toBeVisible();
          const missing = page.locator(
            '[data-download-id="missing"] [data-action="redownload"]',
          );
          confirmAction = false;
          await missing.tap();
          await expect(missing).toBeEnabled();
          assert(
            !commands.some((x) => x.action === "redownload"),
            "declining confirmation restarted missing files",
          );
          confirmAction = true;
          await missing.tap();
          await expect(missing).toHaveCount(0);
          assert.deepEqual(
            commands.map((x) => x.action),
            ["pause", "resume", "redownload"],
          );
          assert(commands[2].confirmed === true);
          if (!blockedStorage)
            await capture({
              path: `${output}/phone-downloads-${name}-${locale}.png`,
              fullPage: true,
            });
          await page.locator("#browse").tap();
          await page.locator("#query").fill("network-error");
          await page.locator("#search button[type=submit]").tap();
          await expect(page.locator("#status[data-kind=error]")).toBeVisible();
          assert(
            !(await page.locator("#status").textContent()).includes(
              "Fixture failure",
            ),
          );
          await page.locator("#retry").tap();
          await expect(page.locator(".card")).toHaveCount(8);
          await page.locator("#connection").tap();
          await expect(page.locator("#device-panel")).toBeVisible();
          await page.locator("[data-theme=dark]").tap();
          await expect(page.locator("html")).toHaveAttribute(
            "data-appearance",
            "dark",
          );
          if (!blockedStorage)
            await capture({
              path: `${output}/phone-device-${name}-${locale}.png`,
              fullPage: true,
            });
          for (const width of [320, 430, 820]) {
            await page.setViewportSize({ width, height: 844 });
            assert(
              await page.evaluate(
                () => document.documentElement.scrollWidth <= innerWidth,
              ),
              "phone UI overflows at " + width,
            );
          }
          await page.setViewportSize({ width: 390, height: 844 });
          await page.locator("#device-magnet").tap();
          await expect(page.locator("#magnet-dialog")).toBeVisible();
          await page.locator("#magnet").fill("not-a-magnet");
          await page
            .locator(
              "#magnet-form button[type=submit],#magnet-form button:not([type])",
            )
            .tap();
          await expect(
            page.locator("#magnet-message[data-kind=error]"),
          ).toBeVisible();
          assert.equal(adds.length, 1, "invalid magnet reached the desktop");
          await page
            .locator("#magnet")
            .fill(
              "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567",
            );
          confirmAction = false;
          await page.locator("#magnet-form button").tap();
          await expect(page.locator("#magnet-form button")).toBeEnabled();
          assert.equal(adds.length, 1);
          confirmAction = true;
          await page.locator("#magnet-form button").tap();
          await expect(page.locator("#magnet-dialog")).not.toBeVisible();
          await expect(activeRow).toBeVisible();
          assert.equal(adds.length, 2);
          if (!blockedStorage) {
            await page.reload();
            await expect(page.locator(".card")).toHaveCount(8);
            await expect(page.locator("html")).toHaveAttribute(
              "data-appearance",
              "dark",
            );
          }
          const dimensions = await page.evaluate(() => ({
            page: document.documentElement.scrollWidth,
            window: innerWidth,
            fonts: [...document.querySelectorAll("input,select,textarea")].map(
              (el) => parseFloat(getComputedStyle(el).fontSize),
            ),
          }));
          assert(
            dimensions.page <= dimensions.window,
            "phone UI overflows horizontally",
          );
          assert(
            dimensions.fonts.every((size) => size >= 16),
            "iOS focus zoom triggered by small control fonts",
          );
          await page.locator("#connection").tap();
          await page.locator("#logout").tap();
          await expect(page.locator("#pair")).toBeVisible();
          assert.deepEqual(errors, []);
          // WebKit's native select shadow tree requests exactly this internal
          // style attribute while parsing each select. The strict CSP blocks it
          // (without affecting the native picker). No application inline style,
          // external resource, or other policy violation is permitted.
          assert.deepEqual(
            policyViolations.filter(
              (event) =>
                !(
                  name === "webkit" &&
                  event.directive === "style-src-attr" &&
                  event.blocked === "inline" &&
                  event.sample === "text-overflow:inherit"
                ),
            ),
            [],
          );
          assert.equal(
            policyMessages.length,
            policyViolations.length,
            "Unclassified CSP console error",
          );
          assert.equal(
            await page.locator("[style]").count(),
            0,
            "Application contains inline style attributes",
          );
        } finally {
          await context.close();
          fixture.stdin.end("\n");
          await new Promise((resolve) => fixture.once("exit", resolve));
        }
      }
    console.log(
      `PASS: ${name}, iPhone RU/EN, light/dark, 320–820px, QR/manual pairing, blocked storage, catalog paging/back/search/retry, release filters, download controls, magnet sheet, theme persistence and logout`,
    );
  } finally {
    await browser.close();
  }
}
