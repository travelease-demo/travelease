/* ==========================================================================
   TravelEase – client behaviour
   - Modal forms loaded with fetch (data-modal-url) and submitted with AJAX
   - Unobtrusive client validation re-parsed for dynamic content
   - Confirmation dialogs (data-confirm-action)
   - Toast notifications, loading indicators, sidebar toggle
   - Live price calculation on the booking form
   ========================================================================== */
(function () {
    "use strict";

    const modalEl = document.getElementById("appModal");
    const modalContent = document.getElementById("appModalContent");
    const appModal = modalEl ? bootstrap.Modal.getOrCreateInstance(modalEl) : null;
    const progress = document.getElementById("page-progress");
    const currency = document.body.dataset.currency || "";

    // ---------- Loading indicator ----------
    const loading = {
        start() { progress.classList.remove("done"); void progress.offsetWidth; progress.classList.add("active"); },
        stop() { progress.classList.remove("active"); progress.classList.add("done"); }
    };

    function setButtonLoading(button, isLoading) {
        if (!button) return;
        if (isLoading) {
            button.dataset.originalHtml = button.innerHTML;
            button.disabled = true;
            const text = button.dataset.loadingText || "Please wait…";
            button.innerHTML = `<span class="spinner-border spinner-border-sm me-2" aria-hidden="true"></span>${text}`;
        } else if (button.dataset.originalHtml) {
            button.disabled = false;
            button.innerHTML = button.dataset.originalHtml;
        }
    }

    // ---------- Toasts ----------
    const toastIcons = { success: "bi-check-circle-fill", error: "bi-x-circle-fill", warning: "bi-exclamation-triangle-fill", info: "bi-info-circle-fill" };

    function showToast(message, type) {
        type = type || "success";
        const container = document.getElementById("toastContainer");
        const el = document.createElement("div");
        el.className = `toast toast-${type}`;
        el.setAttribute("role", type === "error" ? "alert" : "status");
        el.setAttribute("aria-live", "polite");
        el.innerHTML = `<div class="toast-body"><i class="bi ${toastIcons[type] || toastIcons.info}"></i>
            <div class="flex-grow-1"></div><button type="button" class="btn-close btn-close-sm ms-2" data-bs-dismiss="toast" aria-label="Close"></button></div>`;
        el.querySelector(".flex-grow-1").textContent = message;
        container.appendChild(el);
        const toast = new bootstrap.Toast(el, { delay: type === "error" ? 8000 : 4500 });
        el.addEventListener("hidden.bs.toast", () => el.remove());
        toast.show();
    }
    window.TravelEase = { toast: showToast };

    // Server-side TempData toast (after redirects)
    const tc = document.getElementById("toastContainer");
    if (tc && tc.dataset.toastMessage) showToast(tc.dataset.toastMessage, tc.dataset.toastType || "success");

    // ---------- Modal loading ----------
    async function openModal(url) {
        modalContent.innerHTML = `<div class="modal-loading"><div class="spinner-border text-primary mb-3" role="status"></div><div>Loading…</div></div>`;
        appModal.show();
        loading.start();
        try {
            const response = await fetch(url, { headers: { "X-Requested-With": "XMLHttpRequest" } });
            if (!response.ok) throw new Error(`The form could not be loaded (HTTP ${response.status}).`);
            setModalHtml(await response.text());
        } catch (err) {
            appModal.hide();
            showToast(err.message || "Unable to load the form.", "error");
        } finally {
            loading.stop();
        }
    }

    function setModalHtml(html) {
        modalContent.innerHTML = html;
        const form = modalContent.querySelector("form");
        if (form && window.jQuery && jQuery.validator) {
            jQuery(form).removeData("validator").removeData("unobtrusiveValidation");
            jQuery.validator.unobtrusive.parse(form);
        }
        initBookingForm(modalContent);
        const focusable = modalContent.querySelector("[autofocus], .modal-body input:not([type=hidden]), .modal-body select, .modal-body textarea");
        if (focusable) setTimeout(() => focusable.focus(), 200);
    }

    document.addEventListener("click", (e) => {
        const trigger = e.target.closest("[data-modal-url]");
        if (trigger) {
            e.preventDefault();
            openModal(trigger.dataset.modalUrl);
            return;
        }

        const confirmTrigger = e.target.closest("[data-confirm-action]");
        if (confirmTrigger) {
            e.preventDefault();
            openConfirm(confirmTrigger.dataset);
            return;
        }

        const fill = e.target.closest("[data-fill-target]");
        if (fill) {
            const target = document.querySelector(fill.dataset.fillTarget);
            if (target) { target.value = fill.dataset.fillValue; target.dispatchEvent(new Event("input", { bubbles: true })); target.focus(); }
            return;
        }

        // Clickable table rows (ignore clicks on real links / buttons inside the row)
        const row = e.target.closest("tr[data-href]");
        if (row && !e.target.closest("a, button, input, select, label")) {
            window.location.href = row.dataset.href;
        }
    });

    // ---------- AJAX modal form submission ----------
    document.addEventListener("submit", async (e) => {
        const form = e.target;
        if (!form.matches("form[data-ajax-form]")) {
            // Normal forms: show the loading bar and disable the submit button.
            if (form.method.toLowerCase() === "post") {
                loading.start();
                setButtonLoading(e.submitter || form.querySelector("[type=submit]"), true);
            }
            return;
        }

        e.preventDefault();
        if (window.jQuery && jQuery(form).valid && !jQuery(form).valid()) return;

        const button = e.submitter || form.querySelector("[type=submit]");
        setButtonLoading(button, true);
        loading.start();

        try {
            const response = await fetch(form.action, {
                method: "POST",
                body: new FormData(form),
                headers: { "X-Requested-With": "XMLHttpRequest" }
            });
            const contentType = response.headers.get("content-type") || "";

            if (response.ok && contentType.includes("application/json")) {
                const result = await response.json();
                appModal.hide();
                // Navigate (or reload) so the page reflects the change; the toast is delivered via TempData.
                if (result.redirectUrl) window.location.href = result.redirectUrl;
                else window.location.reload();
                return;
            }

            if (response.status === 422 || response.status === 400) {
                setModalHtml(await response.text());
                showToast("Please correct the highlighted fields.", "warning");
                return;
            }

            throw new Error(response.status === 404 ? "The record no longer exists." : `Request failed (HTTP ${response.status}).`);
        } catch (err) {
            showToast(err.message || "Something went wrong. Please try again.", "error");
            setButtonLoading(button, false);
        } finally {
            loading.stop();
        }
    });

    // ---------- Confirmation dialog ----------
    function openConfirm(data) {
        const modal = bootstrap.Modal.getOrCreateInstance(document.getElementById("confirmModal"));
        const form = document.getElementById("confirmForm");
        const button = document.getElementById("confirmButton");
        const icon = document.getElementById("confirmIcon");
        const success = data.confirmVariant === "success";

        form.action = data.confirmAction;
        document.getElementById("confirmReturnUrl").value = window.location.pathname + window.location.search;
        document.getElementById("confirmTitle").textContent = data.confirmTitle || "Are you sure?";
        document.getElementById("confirmMessage").textContent = data.confirmMessage || "";
        button.textContent = data.confirmButton || "Confirm";
        button.className = `btn ${success ? "btn-success" : "btn-danger"}`;
        button.disabled = false;
        icon.className = `confirm-icon mx-auto mb-3${success ? " success" : ""}`;
        icon.innerHTML = `<i class="bi ${success ? "bi-check2-circle" : "bi-exclamation-triangle-fill"}"></i>`;
        modal.show();
    }

    // ---------- Auto-submit filter forms when a select / date changes ----------
    document.querySelectorAll("form[data-auto-submit]").forEach((form) => {
        form.querySelectorAll("select, input[type=date]").forEach((el) => {
            el.addEventListener("change", () => { loading.start(); form.submit(); });
        });
    });

    // Show the progress bar on normal navigation
    window.addEventListener("beforeunload", () => loading.start());
    window.addEventListener("pageshow", () => loading.stop());

    // ---------- Sidebar (mobile) ----------
    document.querySelectorAll("[data-sidebar-toggle]").forEach((b) => b.addEventListener("click", () => document.body.classList.toggle("sidebar-open")));
    document.querySelectorAll("[data-sidebar-close]").forEach((b) => b.addEventListener("click", () => document.body.classList.remove("sidebar-open")));

    // ---------- Booking form: live price calculation ----------
    const formatter = new Intl.NumberFormat("en-IN", { maximumFractionDigits: 2 });
    const money = (v) => `${currency}${formatter.format(v)}`;

    function initBookingForm(root) {
        const form = root.querySelector("form[data-booking-form]");
        if (!form) return;

        const tripSelect = form.querySelector("[data-trip-select]");
        const pax = form.querySelector("[data-passengers]");
        const q = (sel) => form.querySelector(sel);

        function update() {
            const option = tripSelect.selectedOptions[0];
            const passengers = Math.max(0, parseInt(pax.value, 10) || 0);
            const hasTrip = option && option.value !== "0" && option.dataset.price;

            if (!hasTrip) {
                q("[data-summary-route]").textContent = "No trip selected";
                q("[data-summary-departure]").innerHTML = "&nbsp;";
                ["price", "seats", "total"].forEach((k) => (q(`[data-summary-${k}]`).textContent = "—"));
                q("[data-summary-pax]").textContent = passengers || "—";
                q("[data-summary-warning]").classList.add("d-none");
                return;
            }

            const price = parseFloat(option.dataset.price);
            const seats = parseInt(option.dataset.seats, 10);
            q("[data-summary-route]").textContent = option.dataset.route;
            q("[data-summary-departure]").textContent = option.dataset.departure;
            q("[data-summary-price]").textContent = money(price);
            q("[data-summary-pax]").textContent = passengers;
            q("[data-summary-seats]").textContent = Math.max(0, seats - passengers) + " of " + seats;
            q("[data-summary-total]").textContent = money(price * passengers);
            q("[data-summary-warning]").classList.toggle("d-none", passengers <= seats);
            pax.max = Math.min(20, seats);
        }

        form.querySelectorAll("[data-step]").forEach((btn) => {
            btn.addEventListener("click", () => {
                const next = (parseInt(pax.value, 10) || 1) + parseInt(btn.dataset.step, 10);
                pax.value = Math.min(parseInt(pax.max, 10) || 20, Math.max(1, next));
                update();
            });
        });
        tripSelect.addEventListener("change", update);
        pax.addEventListener("input", update);
        update();
    }
})();
