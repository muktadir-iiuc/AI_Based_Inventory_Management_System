(function () {
    "use strict";

    var currentLang = "bn";
    var activeHotspot = null;

    document.addEventListener("DOMContentLoaded", function () {
        initLanguage();
        initNavigation();
        initCounters();
        initProductFilter();
        initProductModal();
        initScrollReveal();
        initTechnologyHotspots();
        initPerformanceBars();
        initContactForm();
        initBackToTop();
    });

    /* ---------------------------------------------------------------------
       Language: Bangla (default) / English toggle
       Bangla copy is authored directly in the Razor views ([data-i18n] /
       [data-i18n-html]); only the English translations live here. The
       original Bangla text is captured lazily on first render so it can be
       restored without duplicating it in this dictionary.
       ------------------------------------------------------------------- */
    var EN_TEXT = {
        "a11y.skip": "Skip to main content",
        "nav.home": "Home", "nav.about": "About Us", "nav.products": "Products",
        "nav.technology": "Technology", "nav.whyus": "Why Bangladesh Tyre",
        "nav.news": "News", "nav.contact": "Contact", "nav.cta": "Find a Dealer",
        "hero.eyebrow": "Engineered For The Road",
        "hero.sub": "Reliable tyre solutions engineered for performance, safety and durability across Bangladesh’s roads.",
        "hero.cta1": "Explore Our Tyres", "hero.cta2": "Discover Bangladesh Tyre",
        "stats.years": "Years of Experience", "stats.dealers": "Dealer Network",
        "stats.tyres": "Tyres on the Road", "stats.nationwide": "Nationwide",
        "stats.coverage": "Service Coverage",
        "stats.disclaimer": "Figures shown are placeholder marketing content pending verified company data.",
        "about.badge": "Trusted Tyre Solutions",
        "about.panel": "Years serving Bangladesh’s roads",
        "about.eyebrow": "About Bangladesh Tyre",
        "about.lead": "Bangladesh Tyre is committed to providing dependable tyre solutions designed around the demands of Bangladesh’s roads, vehicles and customers — from busy city streets to long-haul highway routes.",
        "about.cta": "Learn More About Us",
        "products.eyebrow": "Our Tyre Solutions",
        "filter.all": "All", "filter.passenger": "Passenger", "filter.commercial": "Commercial",
        "filter.cng": "CNG Auto-rickshaw", "filter.motorcycle": "Motorcycle",
        "product.passenger.title": "Passenger Cars",
        "product.passenger.desc": "Comfort, stability and dependable road performance for everyday driving.",
        "product.commercial.title": "Commercial Vehicles",
        "product.commercial.desc": "Built for demanding workloads and long-distance operation.",
        "product.cng.title": "CNG Auto-rickshaw",
        "product.cng.desc": "A reliable, affordable three-wheeler built for Bangladesh’s city and suburban commutes.",
        "product.motorcycle.title": "Motorcycles",
        "product.motorcycle.desc": "Responsive handling and everyday reliability on every route.",
        "product.viewdetails": "View Details",
        "products.disclaimer": "Vehicle photography is for illustrative purposes and does not depict an official Bangladesh Tyre fleet or product fitment.",
        "detail.contactcta": "Contact Us About This Range",
        "whyus.eyebrow": "Why Choose Us", "whyus.title": "Why Choose Bangladesh Tyre?",
        "whyus.card1.title": "Built for Local Roads",
        "whyus.card1.text": "Designed around real-world road conditions across Bangladesh.",
        "whyus.card2.title": "Proven Durability",
        "whyus.card2.text": "Built to deliver dependable performance over time and distance.",
        "whyus.card3.title": "Safety First",
        "whyus.card3.text": "Focus on stability, braking response and road confidence.",
        "whyus.card4.title": "Reliable Performance",
        "whyus.card4.text": "Consistent performance for everyday driving and commercial use.",
        "whyus.card5.title": "Nationwide Support",
        "whyus.card5.text": "Dealer and service network support across Bangladesh.",
        "whyus.card6.title": "Customer Focus",
        "whyus.card6.text": "Committed to building long-term customer relationships.",
        "tech.eyebrow": "Technology",
        "tech.lead": "Explore the engineering concepts behind every Bangladesh Tyre product. Select a point on the tyre to learn more.",
        "perf.eyebrow": "Performance",
        "perf.grip": "Grip", "perf.durability": "Durability", "perf.comfort": "Comfort",
        "perf.mileage": "Mileage", "perf.stability": "Stability",
        "perf.disclaimer": "Visual marketing placeholders — not laboratory-certified ratings.",
        "roads.eyebrow": "Across Bangladesh", "roads.title": "Every Road Has a Story.",
        "roads.city.title": "Dhaka City", "roads.city.text": "Dense city traffic and frequent stop-start driving.",
        "roads.highway.title": "Highways", "roads.highway.text": "Sustained speeds across long inter-district routes.",
        "roads.rural.title": "Rural Roads", "roads.rural.text": "Varied surfaces connecting communities nationwide.",
        "roads.commercial.title": "Commercial Routes", "roads.commercial.text": "Heavy loads moving goods across the country.",
        "roads.distance.title": "Long-Distance Travel", "roads.distance.text": "Extended journeys demanding consistent performance.",
        "cta.lead": "Whether you’re driving across the city or carrying a load across the country, choose a tyre solution built for the road ahead.",
        "cta.finddealer": "Find a Dealer", "cta.contactus": "Contact Us",
        "news.eyebrow": "Insights", "news.title": "News & Insights",
        "news.card1.tag": "Tyre Care", "news.card1.title": "Tyre Care Guide",
        "news.card1.text": "How proper tyre maintenance improves road safety and extends tyre life.",
        "news.card2.tag": "Safety", "news.card2.title": "Driving Safety",
        "news.card2.text": "Essential checks to run through before starting a long journey.",
        "news.card3.tag": "Technology", "news.card3.title": "Tyre Technology",
        "news.card3.text": "Understanding modern tyre construction and what it means for you.",
        "news.viewall": "View All Insights",
        "contact.eyebrow": "Get in Touch", "contact.title": "Let’s Talk",
        "contact.lead": "Have a question about tyres, dealership or support? Reach out and our team will get back to you.",
        "contact.address": "Chattogram - 4100, Bangladesh",
        "contact.hours": "Sat – Thu, 9:00 AM – 6:00 PM",
        "form.name": "Name", "form.email": "Email", "form.phone": "Phone",
        "form.subject": "Subject", "form.message": "Message", "form.send": "Send Message",
        "form.success": "Thank you — your message has been prepared. This is a demo form and no data was actually sent.",
        "form.error.name": "Please enter your name.",
        "form.error.email": "Please enter a valid email address.",
        "form.error.subject": "Please enter a subject.",
        "form.error.message": "Please enter a message (at least 10 characters).",
        "footer.desc": "Reliable tyre solutions engineered for performance, safety and durability across Bangladesh’s roads — from city streets to highway journeys.",
        "footer.quicklinks": "Quick Links", "footer.products": "Products", "footer.support": "Support",
        "footer.dealernetwork": "Dealer Network", "footer.tyrecare": "Tyre Care",
        "footer.copyright": "© 2026 Bangladesh Tyre. All Rights Reserved.",
        "footer.placeholdernote": "All figures, images and contact details on this site are placeholders pending official company information.",
        "whatsapp.label": "Chat with us on WhatsApp"
    };

    var EN_HTML = {
        "hero.title": "Powering Every Journey<br>With Confidence",
        "about.title": "Built for Bangladesh.<br>Driven by Performance.",
        "about.point1": "<strong>Proven Experience</strong> — decades of tyre expertise adapted to local road conditions.",
        "about.point2": "<strong>Quality Commitment</strong> — every tyre built around safety and consistency.",
        "about.point3": "<strong>Customer Focus</strong> — nationwide support built around real customer needs.",
        "about.point4": "<strong>Performance</strong> — engineered for stability, mileage and durability.",
        "products.title": "Engineered to Perform<br>Where the Road Takes You",
        "tech.title": "Engineering Behind<br>Every Revolution",
        "perf.title": "Performance You Can Feel.<br>Confidence You Can Trust.",
        "cta.title": "Find the Right Tyre<br>for Your Journey",
        "detail.passenger.body": "<p>Everyday tyres engineered to balance comfort, stability and efficiency for city driving, family trips and daily commutes.</p><ul><li>Balanced ride comfort on varied road surfaces</li><li>Responsive steering and cornering stability</li><li>Tread compound tuned for fuel efficiency</li><li>Dependable all-season traction</li></ul>",
        "detail.commercial.body": "<p>Built for vans, pickups and light commercial vehicles that carry loads across the city and beyond, day after day.</p><ul><li>Reinforced construction for consistent loads</li><li>Stable handling with a full payload</li><li>Even wear for extended service life</li><li>Reliable performance across fleet duty cycles</li></ul>",
        "detail.cng.body": "<p>Built for Bangladesh’s three-wheeler taxis carrying passengers across busy city and suburban streets, day after day.</p><ul><li>Built for frequent stop-start driving</li><li>Confident control at tight turns and low speeds</li><li>Durable construction for long daily duty cycles</li><li>Reliable performance at an affordable price</li></ul>",
        "detail.motorcycle.body": "<p>Responsive tyres designed for the agility, braking and everyday reliability motorcycle riders depend on.</p><ul><li>Sharp cornering grip and lean stability</li><li>Lightweight, responsive construction</li><li>Confident handling in wet and dry conditions</li><li>Reliable durability for daily commuting</li></ul>"
    };

    function initLanguage() {
        var stored = null;
        try { stored = window.localStorage.getItem("bt-lang"); } catch (e) { /* ignore */ }
        currentLang = stored === "en" ? "en" : "bn";

        document.querySelectorAll("[data-lang-btn]").forEach(function (btn) {
            btn.addEventListener("click", function () {
                setLanguage(btn.getAttribute("data-lang-btn"));
            });
        });

        applyLanguage(currentLang);
    }

    function setLanguage(lang) {
        currentLang = lang === "en" ? "en" : "bn";
        try { window.localStorage.setItem("bt-lang", currentLang); } catch (e) { /* ignore */ }
        applyLanguage(currentLang);
    }

    function applyLanguage(lang) {
        document.documentElement.lang = lang;

        document.querySelectorAll("[data-i18n]").forEach(function (el) {
            if (!el.dataset.i18nBn) el.dataset.i18nBn = el.textContent;
            var key = el.getAttribute("data-i18n");
            el.textContent = lang === "en" && EN_TEXT[key] ? EN_TEXT[key] : el.dataset.i18nBn;
        });

        document.querySelectorAll("[data-i18n-html]").forEach(function (el) {
            if (!el.dataset.i18nBnHtml) el.dataset.i18nBnHtml = el.innerHTML;
            var key = el.getAttribute("data-i18n-html");
            el.innerHTML = lang === "en" && EN_HTML[key] ? EN_HTML[key] : el.dataset.i18nBnHtml;
        });

        document.querySelectorAll("[data-lang-btn]").forEach(function (btn) {
            var isActive = btn.getAttribute("data-lang-btn") === lang;
            btn.classList.toggle("active", isActive);
            btn.setAttribute("aria-pressed", isActive ? "true" : "false");
        });

        document.querySelectorAll("[data-href-bn][data-href-en]").forEach(function (el) {
            el.setAttribute("href", lang === "en" ? el.getAttribute("data-href-en") : el.getAttribute("data-href-bn"));
        });

        renderHotspot(activeHotspot || document.querySelector(".tech-hotspot"));
    }

    /* ---------------------------------------------------------------------
       Navigation: sticky header, mobile menu, smooth scroll, active link
       ------------------------------------------------------------------- */
    function initNavigation() {
        var header = document.getElementById("siteHeader");
        var toggle = document.getElementById("navToggle");
        var menu = document.getElementById("navMenu");
        var navLinks = document.querySelectorAll("[data-nav-link]");

        if (header) {
            var onScroll = function () {
                header.classList.toggle("scrolled", window.scrollY > 40);
            };
            window.addEventListener("scroll", onScroll, { passive: true });
            onScroll();
        }

        if (toggle && menu) {
            toggle.addEventListener("click", function () {
                var isOpen = menu.classList.toggle("open");
                toggle.classList.toggle("open", isOpen);
                toggle.setAttribute("aria-expanded", isOpen ? "true" : "false");
            });

            menu.querySelectorAll("a").forEach(function (link) {
                link.addEventListener("click", function () {
                    menu.classList.remove("open");
                    toggle.classList.remove("open");
                    toggle.setAttribute("aria-expanded", "false");
                });
            });
        }

        if (navLinks.length) {
            var sections = Array.prototype.map.call(navLinks, function (link) {
                var id = link.getAttribute("href").replace("#", "");
                return document.getElementById(id);
            }).filter(Boolean);

            if (sections.length && "IntersectionObserver" in window) {
                var sectionObserver = new IntersectionObserver(function (entries) {
                    entries.forEach(function (entry) {
                        if (entry.isIntersecting) {
                            var activeId = entry.target.id;
                            navLinks.forEach(function (link) {
                                link.classList.toggle("active-link", link.getAttribute("href") === "#" + activeId);
                            });
                        }
                    });
                }, { rootMargin: "-45% 0px -50% 0px" });

                sections.forEach(function (section) {
                    sectionObserver.observe(section);
                });
            }
        }
    }

    /* ---------------------------------------------------------------------
       Animated statistic counters
       ------------------------------------------------------------------- */
    function initCounters() {
        var counters = document.querySelectorAll("[data-counter]");
        if (!counters.length) return;

        var animateCounter = function (el) {
            var target = parseInt(el.getAttribute("data-target"), 10) || 0;
            var suffix = el.getAttribute("data-suffix") || "";
            var duration = 1600;
            var start = null;

            var step = function (timestamp) {
                if (start === null) start = timestamp;
                var progress = Math.min((timestamp - start) / duration, 1);
                var eased = 1 - Math.pow(1 - progress, 3);
                var value = Math.floor(eased * target);
                el.textContent = value + suffix;
                if (progress < 1) {
                    window.requestAnimationFrame(step);
                } else {
                    el.textContent = target + suffix;
                }
            };

            window.requestAnimationFrame(step);
        };

        if (!("IntersectionObserver" in window)) {
            counters.forEach(animateCounter);
            return;
        }

        var observer = new IntersectionObserver(function (entries, obs) {
            entries.forEach(function (entry) {
                if (entry.isIntersecting) {
                    animateCounter(entry.target);
                    obs.unobserve(entry.target);
                }
            });
        }, { threshold: 0.5 });

        counters.forEach(function (counter) {
            observer.observe(counter);
        });
    }

    /* ---------------------------------------------------------------------
       Product category filter
       ------------------------------------------------------------------- */
    function initProductFilter() {
        var buttons = document.querySelectorAll(".filter-btn");
        var cards = document.querySelectorAll(".product-card");
        if (!buttons.length || !cards.length) return;

        var applyFilter = function (category) {
            cards.forEach(function (card) {
                var matches = category === "all" || card.getAttribute("data-category") === category;
                card.classList.toggle("is-hidden", !matches);
            });

            buttons.forEach(function (btn) {
                var isActive = btn.getAttribute("data-filter") === category;
                btn.classList.toggle("active", isActive);
                btn.setAttribute("aria-selected", isActive ? "true" : "false");
            });
        };

        buttons.forEach(function (button) {
            button.addEventListener("click", function () {
                applyFilter(button.getAttribute("data-filter"));
            });
        });

        document.querySelectorAll("[data-filter-link]").forEach(function (link) {
            link.addEventListener("click", function () {
                applyFilter(link.getAttribute("data-filter-link"));
            });
        });
    }

    /* ---------------------------------------------------------------------
       Product "View Details" modal
       ------------------------------------------------------------------- */
    function initProductModal() {
        var triggers = document.querySelectorAll("[data-product-detail]");
        var modal = document.getElementById("productModal");
        if (!triggers.length || !modal) return;

        var imageEl = document.getElementById("productModalImage");
        var titleEl = document.getElementById("productModalTitle");
        var contentEl = document.getElementById("productModalContent");
        var closeEls = modal.querySelectorAll("[data-modal-close]");
        var lastFocused = null;

        var openModal = function (category) {
            var source = document.getElementById("detail-" + category);
            if (!source) return;

            var title = currentLang === "en"
                ? (source.getAttribute("data-title-en") || source.getAttribute("data-title"))
                : source.getAttribute("data-title");

            imageEl.src = source.getAttribute("data-image");
            imageEl.alt = title;
            titleEl.textContent = title;
            contentEl.innerHTML = source.innerHTML;

            lastFocused = document.activeElement;
            modal.hidden = false;
            requestAnimationFrame(function () {
                modal.classList.add("is-open");
            });
            document.body.style.overflow = "hidden";
            modal.querySelector(".product-modal-close").focus();
        };

        var closeModal = function () {
            modal.classList.remove("is-open");
            document.body.style.overflow = "";
            window.setTimeout(function () {
                modal.hidden = true;
            }, 250);
            if (lastFocused) lastFocused.focus();
        };

        triggers.forEach(function (trigger) {
            trigger.addEventListener("click", function () {
                openModal(trigger.getAttribute("data-product-detail"));
            });
        });

        closeEls.forEach(function (el) {
            el.addEventListener("click", closeModal);
        });

        document.addEventListener("keydown", function (event) {
            if (event.key === "Escape" && !modal.hidden) {
                closeModal();
            }
        });
    }

    /* ---------------------------------------------------------------------
       Scroll reveal animations
       ------------------------------------------------------------------- */
    function initScrollReveal() {
        var items = document.querySelectorAll(".reveal");
        if (!items.length) return;

        if (!("IntersectionObserver" in window)) {
            items.forEach(function (item) {
                item.classList.add("is-visible");
            });
            return;
        }

        var observer = new IntersectionObserver(function (entries, obs) {
            entries.forEach(function (entry) {
                if (entry.isIntersecting) {
                    entry.target.classList.add("is-visible");
                    obs.unobserve(entry.target);
                }
            });
        }, { threshold: 0.15 });

        items.forEach(function (item) {
            observer.observe(item);
        });
    }

    /* ---------------------------------------------------------------------
       Technology tyre hotspots
       ------------------------------------------------------------------- */
    function initTechnologyHotspots() {
        var hotspots = document.querySelectorAll(".tech-hotspot");
        var titleEl = document.getElementById("techDetailTitle");
        var textEl = document.getElementById("techDetailText");
        if (!hotspots.length || !titleEl || !textEl) return;

        hotspots.forEach(function (spot) {
            spot.addEventListener("click", function () {
                hotspots.forEach(function (s) { s.classList.remove("active-spot"); });
                spot.classList.add("active-spot");
                activeHotspot = spot;
                renderHotspot(spot);
            });
        });
    }

    function renderHotspot(spot) {
        var titleEl = document.getElementById("techDetailTitle");
        var textEl = document.getElementById("techDetailText");
        if (!spot || !titleEl || !textEl) return;

        titleEl.textContent = currentLang === "en"
            ? (spot.getAttribute("data-title-en") || spot.getAttribute("data-title"))
            : spot.getAttribute("data-title");
        textEl.textContent = currentLang === "en"
            ? (spot.getAttribute("data-text-en") || spot.getAttribute("data-text"))
            : spot.getAttribute("data-text");
    }

    /* ---------------------------------------------------------------------
       Animated performance progress bars
       ------------------------------------------------------------------- */
    function initPerformanceBars() {
        var bars = document.querySelectorAll(".perf-fill");
        if (!bars.length) return;

        var fill = function (bar) {
            var progress = bar.getAttribute("data-progress") || "0";
            bar.style.width = progress + "%";
        };

        if (!("IntersectionObserver" in window)) {
            bars.forEach(fill);
            return;
        }

        var observer = new IntersectionObserver(function (entries, obs) {
            entries.forEach(function (entry) {
                if (entry.isIntersecting) {
                    fill(entry.target);
                    obs.unobserve(entry.target);
                }
            });
        }, { threshold: 0.4 });

        bars.forEach(function (bar) {
            observer.observe(bar);
        });
    }

    /* ---------------------------------------------------------------------
       Contact form: frontend validation + success message (no backend)
       ------------------------------------------------------------------- */
    function initContactForm() {
        var form = document.getElementById("contactForm");
        var successMessage = document.getElementById("formSuccess");
        if (!form || !successMessage) return;

        form.addEventListener("submit", function (event) {
            event.preventDefault();

            var isValid = true;
            var fields = form.querySelectorAll(".form-control[required]");

            fields.forEach(function (field) {
                var group = field.closest(".form-group");
                var fieldValid = field.checkValidity();
                if (!fieldValid) {
                    isValid = false;
                }
                if (group) {
                    group.classList.toggle("is-invalid", !fieldValid);
                }
            });

            if (!isValid) {
                successMessage.hidden = true;
                return;
            }

            successMessage.hidden = false;
            form.reset();
            fields.forEach(function (field) {
                var group = field.closest(".form-group");
                if (group) group.classList.remove("is-invalid");
            });
        });

        form.querySelectorAll(".form-control").forEach(function (field) {
            field.addEventListener("input", function () {
                var group = field.closest(".form-group");
                if (group && field.checkValidity()) {
                    group.classList.remove("is-invalid");
                }
            });
        });
    }

    /* ---------------------------------------------------------------------
       Back to top button
       ------------------------------------------------------------------- */
    function initBackToTop() {
        var button = document.getElementById("backToTop");
        if (!button) return;

        window.addEventListener("scroll", function () {
            button.classList.toggle("visible", window.scrollY > 500);
        }, { passive: true });

        button.addEventListener("click", function () {
            window.scrollTo({ top: 0, behavior: "smooth" });
        });
    }
})();
