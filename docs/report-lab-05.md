# Лабораторна робота 5

## Безпека браузера XSS DOM і межі CORS

Чекотун Богдан Дмитрович, КБ-42. Рівень виконання — «Добре».

## Мета

Дослідити інтерпретацію збереженого тексту браузером, усунути небезпечні DOM sinks та перевірити межі CORS, credentials і CSP на локальному SecureLab.

## Результати XSS

Description із форми зберігався в PostgreSQL та повертався в JSON. У browser-lab.js значення передавалося в innerHTML, що створювало HTML-елементи. Канонічний локальний marker установив data-lab05-marker="executed" у сеансі Alice, а потім при перегляді того самого запису Morgan. Публічний marker-коментар у seed відтворив аналогічний результат.

Обидва присвоєння innerHTML замінено спільним createTextElement із textContent. Структуру створює код; користувацький рядок потрапляє в текстовий вузол. Retest показав буквальне відображення marker, відсутність img і marker-ефекту. Порівняння Payload/response JSON показало збереження вхідного marker. Після reset опис створено повторно з новим Guid; різні записи не ототожнюються.

Перевірено лапки, символи < і >, Київ — Ω та HTML-подібний текст. До fix **жирний** текст форматувався; після fix теги відображаються буквально. Довгий рядок 3900 символів показано до fix; окремого знімка такого повтору після fix немає.

## Review XSS CORS CSRF та authorization

XSS виникає на межі інтерпретації недовірених даних як коду або розмітки. У цьому flow першопричиною був innerHTML, а захистом став textContent. Сам запис у БД, JSON escaping і перевірка ролі не змінюють вимог до sink.

CORS регулює доступ JavaScript сторінки іншого origin до response. Development-policy дозволяє лише http://localhost:4173; 4174 залишається негативним контролем. Non-safelisted X-Lab-Cors-Probe спричиняє browser preflight.

Authorization перевіряє право principal виконати дію над ресурсом. Навіть дозволений origin без чинної cookie отримує 401. Успішний preflight не надає користувачу права на інцидент.

CSRF стосується небажаних запитів від імені користувача з автоматично доданими credentials. Проста cross-origin дія може дійти до API, навіть коли відповідь недоступна сторінці. Тому CORS не замінює CSRF-захист; додавання antiforgery належить до наступної роботи.

Порти localhost утворюють різні origins, але залишаються same-site. Cookie-дослід не доводить cross-site поведінку SameSite. AllowCredentials і credentials: include потрібні разом для читання credentialed cross-origin response; точний allowlist зберігається.

## CSP і заголовки

Enforcement CSP дозволяє власні scripts/styles/connect/images, забороняє object і вбудовування через frame-ancestors none, обмежує base-uri. Завантаження контрольного зображення з 4173 заблоковано директивою img-src self. Основний клієнт під policy продовжив відображати список, деталі та створювати інцидент.

nosniff обмежує MIME sniffing, але не замінює правильний Content-Type. no-referrer обрано через відсутність потреби передавати адресу сторінки іншим ресурсам. frame-ancestors none зменшує ризик clickjacking. Для Development /scalar передбачено окремий виняток, оскільки це сторонній довідковий UI. CSP не підміняє safe DOM: img у DOM з заблокованим handler усе одно означав би невиправлений sink.

## Git і обмеження доказів

* Vulnerable scaffold: 695b71b09a4ddadfcad6e2c8f851a11fcd31d94c.
* Seed коментаря: d5c54e3ae8b4de9ee23ea0984a7f97880df18244.
* Safe DOM commit: 8a6915a0ced5eab046f9960179efe3a0ff1608e0.

## Відповіді на контрольні питання

1. **Чому БД не робить value довіреним?** БД зберігає рядок, але не визначає безпечний контекст його інтерпретації браузером.
2. **Source, transformation, sink?** Поле description → JSON, validation, Trim, БД і response.json → до fix innerHTML, після fix textContent.
3. **Stored, reflected, DOM-based?** Stored зберігається й читається пізніше; reflected повертається в поточній відповіді; DOM-based описує небезпечне клієнтське перетворення. У досліді дані збережені, а HTML parsing виконує client sink.
4. **Чому блокування script недостатньо?** Код може виконуватися через event handler, як onerror у marker.
5. **Коли textContent, коли sanitizer?** Plain text — textContent; sanitizer потрібний лише за вимоги обмеженого formatted HTML. У цьому завданні HTML не дозволено.
6. **Чому не regex sanitizer?** HTML має складні правила parsing і contexts; рядкова blacklist не моделює їх коректно.
7. **Компоненти origin?** Scheme, host, port.
8. **Що таке preflight?** Автоматичний OPTIONS браузера з Origin і Access-Control-Request-\* перед певними cross-origin запитами.
9. **Хто задає і застосовує CORS?** API задає дозволи response headers; браузер застосовує їх до доступу JavaScript.
10. **Чому curl читає відповідь?** Він не застосовує browser SOP, проте API все одно перевіряє credentials і права.
11. **Чому CORS не authorization або CSRF?** Він не визначає права principal і не гарантує відсутність надісланого state-changing request.
12. **Чому не wildcard із credentials?** Credentialed читання потребує конкретного дозволеного origin; довіряти всім origins неприпустимо.
13. **Межа CSP?** Це додаткові обмеження браузера, які можуть блокувати handler, але не перетворюють innerHTML на текстовий sink.
14. **nosniff, Referrer-Policy, frame-ancestors?** Обмежують MIME sniffing, передавання referrer і вбудовування/clickjacking відповідно.

## Висновок

Дані з API залишаються недовіреними для HTML sink незалежно від автентифікації та авторизації. Заміна innerHTML на textContent усунула виконання marker для опису й коментаря зі збереженням рядків у JSON. Досліди CORS відокремили browser-доступ до response від API-доступу до ресурсу: 4173 читає дозволені відповіді, 4174 не має такого дозволу, а direct client не застосовує SOP. CSP заблокувала контрольний зовнішній image request без зупинки основного flow.

