// Repeating form rows (products, competitors). Indexes are renumbered so MVC model binding sees 0..n-1.
document.querySelectorAll("[data-repeater]").forEach(box => {
  const name = box.dataset.repeater;
  const tpl = document.getElementById("row-" + name);
  const rows = () => [...box.querySelectorAll(".repeater__row:not(.repeater__head)")];
  const renumber = () => rows().forEach((row, i) =>
    row.querySelectorAll("[name]").forEach(el => el.name = el.name.replace(/\[(\d+|__i__)\]/, `[${i}]`)));

  box.querySelector("[data-add]").addEventListener("click", e => {
    const row = tpl.content.firstElementChild.cloneNode(true);
    e.currentTarget.before(row);
    renumber();
    row.querySelector("input").focus();
  });

  box.addEventListener("click", e => {
    if (!e.target.matches("[data-remove]")) return;
    const row = e.target.closest(".repeater__row");
    if (rows().length > 1) row.remove();
    else row.querySelectorAll("input").forEach(i => i.value = "");
    renumber();
  });
});
