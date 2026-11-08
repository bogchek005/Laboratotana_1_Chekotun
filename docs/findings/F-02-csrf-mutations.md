# Finding F-02 — Недостатня antiforgery validation на cookie-authenticated mutations

**Категорія:** CSRF / browser-origin trust  
**Пріоритет:** Помірний або високий залежно від доступності mutation endpoint і наслідків зміни інциденту  
**Стан:** Захист додано; основні показані сценарії повторно перевірено.

## Reproduction

У базовій версії маршрути PUT/DELETE інцидентів були зареєстровані без явного `AntiforgeryFilter`. Для фінального підтвердження початкового дефекту слід прив'язати цей стан до точного baseline commit і показати результат контрольованого запиту до версії до виправлення. Сам факт наявності cookie недостатній для висновку про вразливість без маршруту та відтворення.

## Impact

За відсутності antiforgery validation браузер із чинною cookie-сесією може надсилати state-changing requests у контексті користувача, що створює ризик несанкціонованої зміни або видалення інциденту за відповідних передумов. CSRF-контроль не замінює перевірку прав на ресурс.

## Root cause

Для JSON/DELETE cookie-authenticated mutations потрібна явна перевірка CSRF request token у відповідних endpoint; сама наявність cookie authentication не забезпечує такого захисту.

## Fix

1. Налаштовано `AddAntiforgery` із заголовком `X-CSRF-TOKEN` і cookie `SecureLab.Antiforgery`.
2. Додано `GET /api/auth/csrf-token` з JSON-полем `token`.
3. Реалізовано `AntiforgeryFilter`, що перехоплює `AntiforgeryValidationException` та повертає HTTP 400.
4. Підключено фільтр на змінювальні маршрути; для PUT/DELETE показано `.AddEndpointFilter<AntiforgeryFilter>()`.
5. Додано `SecureLabCsrf` у браузерний клієнт із підтримкою request token та reset при зміні сеансу.

Рисунки 21–47.

## Retest

* POST без токена → 400, новий інцидент не створено: рисунки 48–52.
* PUT без токена → 400, title/description не змінилися: рисунки 60–61.
* DELETE без токена → 400, інцидент залишився доступним: рисунки 62–64.
* Logout без токена → 400, сесія збереглася: рисунки 65–67.
* Triage без токена → 400, статус залишився New: рисунки 70–72.

## Positive regression

* Отримано token через справжній endpoint: рисунок 53.
* POST із токеном → 201, запис з'явився і читається: рисунки 54–57.
* Logout із токеном → 204, після цього `/api/me` → 401: рисунки 68–69.
* Дозволений triage із токеном → 204, новий статус Triaged: рисунки 73–74.

## Residual risk

* Не всі browser-accessible mutations автоматично покриті лише наявністю одного filter; слід регулярно переглядати endpoint registry.
* CSRF не замінює XSS-захист, permission checks і IDOR-тести.
* Не зафіксовано повного фінального `dotnet test` після останніх змін.
* Для доказу finding «до виправлення» потрібне посилання на точний baseline hash у локальному Git, без зовнішнього URL.

## Evidence

Рисунки 21–28, 46–74.

