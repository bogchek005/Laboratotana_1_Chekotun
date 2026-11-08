window.probeRows = [];

window.runProbe = async function (url, mode = "default")
{
    const target = new URL(url, location.href);
    const options = {};

    if (mode != "default")
    {
        options.credentials = mode;
    }

    const row =
    {
        id: window.probeRows.length + 1,
        pageOrigin: location.origin,
        resource: target.href,
        resourceType: target.pathname === "/health" ? "public" : "protected",
        credentials: mode === "default" ? "default (same-origin)" : mode,
        cookiePresent: "не перевірено",
        status: null,
        responseReadable: false
    };

    try
    {
        const response = await fetch(url, options);

        row.status = response.status;
        row.responseReadable = true;
    }
    catch
    {
        row.error = "Response недоступний Javascript; перевірити Network";
    }
    window.probeRows.push(row);
    console.table(window.probeRows);

    return row;
};

window.markCookie = function (id, present)
{
    const row = window.probeRows.find(item => item.id === id);

    if (!row)
    {
        throw new Error("Невідомий номер проби");
    }

    row.cookiePresent = present ? "так" : "ні";

    console.table(window.probeRows);
};

document.querySelector("#probe")?.addEventListener("click", async () =>
{
    const url = document.querySelector("#url").value;
    const mode = document.querySelector("#credentials").value;

    const row = await window.runProbe(url, mode);

    document.querySelector("#result").textContent = JSON.stringify(row, null, 2);
});