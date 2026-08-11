function betterSubtitleExtractorController(view) {
    const PLUGIN_ID = "77BE2143-68BE-4E77-AFC8-82859969038A";

    /*** Helpers ***/
    const $ = (sel, ctx) => (ctx || document).querySelector(sel);
    const $$ = (sel, ctx) => [...(ctx || document).querySelectorAll(sel)];

    let activeTabId = "general";
    let currentConfig = {};
    let isDirty = false;
    let configSnapshot = null;
    let librariesCache = [];

    /*** Checkbox list builder ***/
    function buildCheckList(items, selected, opts) {
        opts = opts || {};
        const container = document.createElement("div");
        container.className = "bse-list-block";

        // Search input
        const searchInput = document.createElement("input");
        searchInput.type = "text";
        searchInput.className = "bse-search-input";
        searchInput.placeholder = opts.searchPlaceholder || "Search...";
        searchInput.addEventListener("input", (e) => {
            const query = e.target.value.toLowerCase();
            $$(".bse-check-list-item", container).forEach(item => {
                const label = item.querySelector("span")?.textContent?.toLowerCase() || "";
                item.classList.toggle("hidden", !label.includes(query));
            });
        });
        container.appendChild(searchInput);

        // Select All / Select None buttons
        const btnBar = document.createElement("div");
        btnBar.className = "bse-btn-bar";

        const btnSelectAll = document.createElement("button");
        btnSelectAll.type = "button";
        btnSelectAll.className = "bse-action-button";
        btnSelectAll.textContent = "Select All";
        btnSelectAll.addEventListener("click", () => {
            $$("input[type=checkbox]", container).forEach(cb => { cb.checked = true; });
            markDirty();
        });

        const btnSelectNone = document.createElement("button");
        btnSelectNone.type = "button";
        btnSelectNone.className = "bse-action-button";
        btnSelectNone.textContent = "Select None";
        btnSelectNone.addEventListener("click", () => {
            $$("input[type=checkbox]", container).forEach(cb => { cb.checked = false; });
            markDirty();
        });

        btnBar.appendChild(btnSelectAll);
        btnBar.appendChild(btnSelectNone);
        container.appendChild(btnBar);

        // Check list
        const checkList = document.createElement("div");
        checkList.className = "paperList checkboxList checkboxList-paperList bse-check-list";

        for (const item of items) {
            const label = document.createElement("label");
            label.className = "bse-check-list-item";

            const cb = document.createElement("input");
            cb.setAttribute("is", "emby-checkbox");
            cb.type = "checkbox";
            cb.dataset.value = item.Value;
            cb.checked = selected.includes(item.Value);
            cb.addEventListener("change", markDirty);

            const span = document.createElement("span");
            span.className = "checkboxLabel";
            span.textContent = item.Text;

            label.appendChild(cb);
            label.appendChild(span);
            checkList.appendChild(label);
        }

        container.appendChild(checkList);
        return container;
    }

    function getCheckedValues(container) {
        return $$("input[type=checkbox]:checked", container).map(cb => cb.dataset.value);
    }

    /*** Dirty tracking ***/
    function takeSnapshot() {
        configSnapshot = JSON.parse(JSON.stringify(gatherConfig()));
        isDirty = false;
        const indicator = document.getElementById("dirtyIndicator");
        if (indicator) indicator.style.display = "none";
        const banner = document.getElementById("tabWarningBanner");
        if (banner) banner.style.display = "none";
    }

    function markDirty() {
        isDirty = true;
        const indicator = document.getElementById("dirtyIndicator");
        if (indicator) indicator.style.display = "inline";
        const banner = document.getElementById("tabWarningBanner");
        if (banner) banner.style.display = "flex";
    }

    /*** Library loading ***/
    async function loadLibraries() {
        try {
            const libs = await window.ApiClient.getVirtualFolders();
            // Match the upstream behavior: only movies/tvshows (or untyped) libraries are shown.
            librariesCache = libs
                .filter((item) => item.CollectionType === undefined || item.CollectionType === "tvshows" || item.CollectionType === "movies")
                .map(l => ({ Value: l.Name, Text: l.Name }));
        } catch (e) {
            console.warn("Better Subtitle Extractor: Failed to load libraries", e);
            librariesCache = [];
        }
    }

    /*** Tab definitions ***/
    const tabs = [
        {
            id: "general",
            label: "General",
            render(container) {
                const section = document.createElement("div");

                const title = document.createElement("h3");
                title.className = "bse-section-header";
                title.textContent = "General Settings";
                section.appendChild(title);

                // Extraction during library scan
                const scanDiv = document.createElement("div");
                scanDiv.className = "inputContainer";
                scanDiv.innerHTML = `
                    <label class="checkboxContainer">
                        <input is="emby-checkbox" type="checkbox" id="chkEnableDuringScan"/>
                        <span>Extract subtitles and attachments during library scan</span>
                    </label>
                    <span class="bse-field-description">This will make sure subtitles and attachments are extracted sooner but will result in longer library scans. Does not disable the scheduled task.</span>`;
                section.appendChild(scanDiv);

                // Worker threads
                const workerDiv = document.createElement("div");
                workerDiv.className = "inputContainer bse-worker-section";
                workerDiv.style.marginTop = "16px";
                workerDiv.innerHTML = `
                    <input is="emby-input" type="number" id="txtWorkerThreads" label="Worker Threads:" min="1" max="32" step="1"/>
                    <span class="bse-field-description">Number of parallel worker threads for subtitle extraction tasks. Default: 1.</span>`;
                section.appendChild(workerDiv);

                container.appendChild(section);
            }
        },
        {
            id: "libraries",
            label: "Libraries",
            render(container) {
                const section = document.createElement("div");

                const title = document.createElement("h3");
                title.className = "bse-section-header";
                title.textContent = "Library Filters";
                section.appendChild(title);

                const desc = document.createElement("p");
                desc.className = "bse-section-desc";
                desc.textContent = "Limit extraction to specific libraries. If nothing is selected, all libraries will be included.";
                section.appendChild(desc);

                // Subtitle libraries
                const subLabel = document.createElement("div");
                subLabel.className = "bse-list-label";
                subLabel.textContent = "Subtitle extraction libraries";
                section.appendChild(subLabel);
                section.appendChild(buildCheckList(
                    librariesCache,
                    currentConfig.SelectedSubtitlesLibraries || [],
                    { searchPlaceholder: "Search libraries..." }
                ));

                // Attachment libraries
                const attLabel = document.createElement("div");
                attLabel.className = "bse-list-label";
                attLabel.style.marginTop = "16px";
                attLabel.textContent = "Attachment extraction libraries";
                section.appendChild(attLabel);
                section.appendChild(buildCheckList(
                    librariesCache,
                    currentConfig.SelectedAttachmentsLibraries || [],
                    { searchPlaceholder: "Search libraries..." }
                ));

                container.appendChild(section);
            }
        },
        {
            id: "languages",
            label: "Languages",
            render(container) {
                const section = document.createElement("div");

                const title = document.createElement("h3");
                title.className = "bse-section-header";
                title.textContent = "Subtitle Languages";
                section.appendChild(title);

                // Master checkbox
                const masterDiv = document.createElement("div");
                masterDiv.className = "bse-master-checkbox";
                masterDiv.innerHTML = `
                    <label class="checkboxContainer">
                        <input is="emby-checkbox" type="checkbox" id="chkExtractAllLanguages"/>
                        <span>Extract all languages</span>
                    </label>
                    <span class="bse-field-description">When enabled, subtitles in all languages will be extracted. Disable to select specific languages below.</span>`;
                section.appendChild(masterDiv);

                // Grid container
                const gridContainer = document.createElement("div");
                gridContainer.className = "bse-grid-container";
                gridContainer.id = "languageGridContainer";
                gridContainer.appendChild(buildCheckList(
                    currentConfig.AllLanguages || [],
                    currentConfig.SelectedLanguages || [],
                    { searchPlaceholder: "Search languages..." }
                ));
                section.appendChild(gridContainer);

                container.appendChild(section);
            }
        },
        {
            id: "types",
            label: "Subtitle Types",
            render(container) {
                const section = document.createElement("div");

                const title = document.createElement("h3");
                title.className = "bse-section-header";
                title.textContent = "Subtitle Types";
                section.appendChild(title);

                // Master checkbox
                const masterDiv = document.createElement("div");
                masterDiv.className = "bse-master-checkbox";
                masterDiv.innerHTML = `
                    <label class="checkboxContainer">
                        <input is="emby-checkbox" type="checkbox" id="chkExtractAllCodecTypes"/>
                        <span>Extract all subtitle types</span>
                    </label>
                    <span class="bse-field-description">When enabled, all subtitle formats will be extracted. Disable to select specific types below.</span>`;
                section.appendChild(masterDiv);

                // Grid container
                const gridContainer = document.createElement("div");
                gridContainer.className = "bse-grid-container";
                gridContainer.id = "codecGridContainer";
                gridContainer.appendChild(buildCheckList(
                    currentConfig.AllSubtitleCodecs || [],
                    currentConfig.SelectedCodecTypes || [],
                    { searchPlaceholder: "Search subtitle types..." }
                ));
                section.appendChild(gridContainer);

                container.appendChild(section);
            }
        },
        {
            id: "regex",
            label: "Regex & Overrides",
            render(container) {
                const section = document.createElement("div");
                section.className = "bse-regex-section";

                const title = document.createElement("h3");
                title.className = "bse-section-header";
                title.textContent = "Title Regex Filters";
                section.appendChild(title);

                // Accept regex
                const acceptDiv = document.createElement("div");
                acceptDiv.className = "inputContainer";
                acceptDiv.innerHTML = `
                    <label class="inputLabel" for="txtAcceptRegex">Accept Pattern (only extract subtitles whose title matches this regex)</label>
                    <input is="emby-input" type="text" id="txtAcceptRegex" placeholder="e.g. ^English|^Commentary"/>
                    <span class="bse-field-description">Leave empty to accept all. Only subtitles with titles matching this pattern will be extracted.</span>`;
                section.appendChild(acceptDiv);

                // Reject regex
                const rejectDiv = document.createElement("div");
                rejectDiv.className = "inputContainer";
                rejectDiv.innerHTML = `
                    <label class="inputLabel" for="txtRejectRegex">Reject Pattern (skip subtitles whose title matches this regex)</label>
                    <input is="emby-input" type="text" id="txtRejectRegex" placeholder="e.g. Signs|Songs|Forced"/>
                    <span class="bse-field-description">Leave empty to reject none. Subtitles with titles matching this pattern will be skipped. <strong>Rejection takes precedence over acceptance.</strong></span>`;
                section.appendChild(rejectDiv);

                // Override expressions
                const overrideTitle = document.createElement("h3");
                overrideTitle.className = "bse-section-header";
                overrideTitle.style.marginTop = "24px";
                overrideTitle.textContent = "Override Expressions";
                section.appendChild(overrideTitle);

                const overrideDesc = document.createElement("p");
                overrideDesc.className = "bse-section-desc";
                overrideDesc.innerHTML = `Boolean expressions using C# syntax that override the normal filters above. Available variables:
                    <strong>LANGUAGE</strong> (e.g. "eng", "jpn", "und"),
                    <strong>TYPE</strong> (e.g. "subrip", "ass", "PGSSUB"),
                    <strong>TITLE</strong> (the subtitle track title).
                    You can use standard C# string methods and operators: <code>==</code>, <code>!=</code>, <code>&&</code>, <code>||</code>, <code>!</code>,
                    <code>.Contains()</code>, <code>.StartsWith()</code>, <code>.EndsWith()</code>.`;
                section.appendChild(overrideDesc);

                // Accept override
                const acceptOverrideDiv = document.createElement("div");
                acceptOverrideDiv.style.marginBottom = "1.5em";
                acceptOverrideDiv.innerHTML = `
                    <div class="checkboxContainer" style="margin-bottom: 0.5em;">
                        <label>
                            <input is="emby-checkbox" type="checkbox" id="chkAcceptOverride"/>
                            <span>Enable Accept Override</span>
                        </label>
                        <span class="bse-field-description">When enabled, subtitles matching this expression are <strong>always extracted</strong>, bypassing all other filters.</span>
                    </div>
                    <textarea is="emby-input" id="txtAcceptOverride" rows="3" style="width: 100%; font-family: monospace; resize: vertical;"
                        placeholder='e.g. LANGUAGE == "eng" && TYPE == "subrip"'></textarea>`;
                section.appendChild(acceptOverrideDiv);

                // Reject override
                const rejectOverrideDiv = document.createElement("div");
                rejectOverrideDiv.style.marginBottom = "1.5em";
                rejectOverrideDiv.innerHTML = `
                    <div class="checkboxContainer" style="margin-bottom: 0.5em;">
                        <label>
                            <input is="emby-checkbox" type="checkbox" id="chkRejectOverride"/>
                            <span>Enable Reject Override</span>
                        </label>
                        <span class="bse-field-description">When enabled, subtitles matching this expression are <strong>always skipped</strong>, regardless of all other filters. Takes precedence over accept override.</span>
                    </div>
                    <textarea is="emby-input" id="txtRejectOverride" rows="3" style="width: 100%; font-family: monospace; resize: vertical;"
                        placeholder='e.g. TITLE.Contains("Signs") || TITLE.Contains("Songs")'></textarea>`;
                section.appendChild(rejectOverrideDiv);

                container.appendChild(section);
            }
        },
        {
            id: "output",
            label: "Output",
            render(container) {
                const section = document.createElement("div");

                const title = document.createElement("h3");
                title.className = "bse-section-header";
                title.textContent = "External Subtitle Output";
                section.appendChild(title);

                const desc = document.createElement("p");
                desc.className = "bse-section-desc";
                desc.textContent = "Controls how extracted subtitles are written as external files next to the media.";
                section.appendChild(desc);

                // Include default marker
                const defaultDiv = document.createElement("div");
                defaultDiv.className = "inputContainer";
                defaultDiv.innerHTML = `
                    <label class="checkboxContainer">
                        <input is="emby-checkbox" type="checkbox" id="chkDefaultMarker"/>
                        <span>Include "default" marker in filename</span>
                    </label>
                    <span class="bse-field-description">Adds a ".default" marker for the default subtitle stream (e.g. Movie.default.eng.srt).</span>`;
                section.appendChild(defaultDiv);

                // Exclude SDH
                const sdhDiv = document.createElement("div");
                sdhDiv.className = "inputContainer";
                sdhDiv.style.marginTop = "16px";
                sdhDiv.innerHTML = `
                    <label class="checkboxContainer">
                        <input is="emby-checkbox" type="checkbox" id="chkExcludeSdh"/>
                        <span>Exclude SDH subtitles</span>
                    </label>
                    <span class="bse-field-description">Skip subtitles flagged as hearing impaired (SDH).</span>`;
                section.appendChild(sdhDiv);

                // Exclude forced
                const forcedDiv = document.createElement("div");
                forcedDiv.className = "inputContainer";
                forcedDiv.style.marginTop = "16px";
                forcedDiv.innerHTML = `
                    <label class="checkboxContainer">
                        <input is="emby-checkbox" type="checkbox" id="chkExcludeForced"/>
                        <span>Exclude forced subtitles</span>
                    </label>
                    <span class="bse-field-description">Skip subtitles flagged as forced.</span>`;
                section.appendChild(forcedDiv);

                // Overwrite existing
                const overwriteDiv = document.createElement("div");
                overwriteDiv.className = "inputContainer";
                overwriteDiv.style.marginTop = "16px";
                overwriteDiv.innerHTML = `
                    <label class="checkboxContainer">
                        <input is="emby-checkbox" type="checkbox" id="chkOverwrite"/>
                        <span>Overwrite existing files</span>
                    </label>
                    <span class="bse-field-description">When enabled, existing external subtitle files are replaced. When disabled, existing files are left untouched.</span>`;
                section.appendChild(overwriteDiv);

                // Convert to SRT
                const convertSrtDiv = document.createElement("div");
                convertSrtDiv.className = "inputContainer";
                convertSrtDiv.style.marginTop = "16px";
                convertSrtDiv.innerHTML = `
                    <label class="checkboxContainer">
                        <input is="emby-checkbox" type="checkbox" id="chkConvertToSrt"/>
                        <span>Convert to SRT</span>
                    </label>
                    <span class="bse-field-description">Convert text-based subtitles to SRT format. Image-based subtitles are not affected.</span>`;
                section.appendChild(convertSrtDiv);

                container.appendChild(section);
            }
        }
    ];

    /*** Tab switching ***/
    function switchTab(tabId) {
        // Persist edits from the outgoing tab before the DOM is discarded.
        const content = document.getElementById("bseTabContent");
        if (!content) {
            return;
        }

        if (content.childElementCount > 0) {
            currentConfig = gatherConfig();
        }

        activeTabId = tabId;
        const nav = document.getElementById("bseTabNav");
        content.innerHTML = "";

        // Update button states
        $$(".bse-tab-button", nav).forEach(btn => {
            btn.classList.toggle("tab-active", btn.dataset.tabId === tabId);
        });

        const tab = tabs.find(t => t.id === tabId);
        if (tab) tab.render(content);

        // Repopulate fields after render
        populateTab(tabId);
    }

    function populateTab(tabId) {
        const config = currentConfig;

        if (tabId === "general") {
            const chk = document.getElementById("chkEnableDuringScan");
            if (chk) chk.checked = !!config.ExtractionDuringLibraryScan;
            const threads = document.getElementById("txtWorkerThreads");
            if (threads) threads.value = config.WorkerThreads || 1;
        } else if (tabId === "languages") {
            const master = document.getElementById("chkExtractAllLanguages");
            if (master) {
                master.checked = config.ExtractAllLanguages !== false;
                master.addEventListener("change", () => updateGridState(master, "languageGridContainer"));
                updateGridState(master, "languageGridContainer");
            }
        } else if (tabId === "types") {
            const master = document.getElementById("chkExtractAllCodecTypes");
            if (master) {
                master.checked = config.ExtractAllCodecTypes !== false;
                master.addEventListener("change", () => updateGridState(master, "codecGridContainer"));
                updateGridState(master, "codecGridContainer");
            }
        } else if (tabId === "regex") {
            const acceptRegex = document.getElementById("txtAcceptRegex");
            if (acceptRegex) acceptRegex.value = config.AcceptTitleRegex || "";
            const rejectRegex = document.getElementById("txtRejectRegex");
            if (rejectRegex) rejectRegex.value = config.RejectTitleRegex || "";
            const chkAccept = document.getElementById("chkAcceptOverride");
            if (chkAccept) chkAccept.checked = !!config.AcceptOverrideEnabled;
            const txtAccept = document.getElementById("txtAcceptOverride");
            if (txtAccept) txtAccept.value = config.AcceptOverrideExpression || "";
            const chkReject = document.getElementById("chkRejectOverride");
            if (chkReject) chkReject.checked = !!config.RejectOverrideEnabled;
            const txtReject = document.getElementById("txtRejectOverride");
            if (txtReject) txtReject.value = config.RejectOverrideExpression || "";
        } else if (tabId === "output") {
            const chkDefault = document.getElementById("chkDefaultMarker");
            if (chkDefault) chkDefault.checked = !!config.IncludeDefaultMarker;
            const chkSdh = document.getElementById("chkExcludeSdh");
            if (chkSdh) chkSdh.checked = !!config.ExcludeSdh;
            const chkForced = document.getElementById("chkExcludeForced");
            if (chkForced) chkForced.checked = !!config.ExcludeForced;
            const chkOverwrite = document.getElementById("chkOverwrite");
            if (chkOverwrite) chkOverwrite.checked = !!config.OverwriteExisting;
            const chkConvertToSrt = document.getElementById("chkConvertToSrt");
            if (chkConvertToSrt) chkConvertToSrt.checked = !!config.ConvertToSrt;
        }
    }

    function updateGridState(master, containerId) {
        const container = document.getElementById(containerId);
        if (container) {
            container.classList.toggle("disabled", master.checked);
        }
    }

    /*** Gather config from DOM ***/
    function gatherConfig() {
        const config = JSON.parse(JSON.stringify(currentConfig));

        // General
        const chkScan = document.getElementById("chkEnableDuringScan");
        if (chkScan) config.ExtractionDuringLibraryScan = chkScan.checked;
        const threads = document.getElementById("txtWorkerThreads");
        if (threads) {
            const val = parseInt(threads.value, 10);
            config.WorkerThreads = isNaN(val) || val < 1 ? 1 : Math.min(val, 32);
        }

        // Libraries (only read from DOM when the Libraries tab is active)
        if (activeTabId === "libraries") {
            const libBlocks = $$(".bse-list-block", document.getElementById("bseTabContent"));
            if (libBlocks.length >= 2) {
                config.SelectedSubtitlesLibraries = getCheckedValues(libBlocks[0]);
                config.SelectedAttachmentsLibraries = getCheckedValues(libBlocks[1]);
            }
        }

        // Languages (only read from DOM when the Languages tab is active)
        if (activeTabId === "languages") {
            const chkAllLangs = document.getElementById("chkExtractAllLanguages");
            if (chkAllLangs) {
                config.ExtractAllLanguages = chkAllLangs.checked;
                const langContainer = document.getElementById("languageGridContainer");
                if (langContainer) config.SelectedLanguages = getCheckedValues(langContainer);
            }
        }

        // Codec types (only read from DOM when the Subtitle Types tab is active)
        if (activeTabId === "types") {
            const chkAllCodecs = document.getElementById("chkExtractAllCodecTypes");
            if (chkAllCodecs) {
                config.ExtractAllCodecTypes = chkAllCodecs.checked;
                const codecContainer = document.getElementById("codecGridContainer");
                if (codecContainer) config.SelectedCodecTypes = getCheckedValues(codecContainer);
            }
        }

        // Regex (only read from DOM when the Regex & Overrides tab is active)
        if (activeTabId === "regex") {
            const acceptRegex = document.getElementById("txtAcceptRegex");
            if (acceptRegex) config.AcceptTitleRegex = acceptRegex.value.trim();
            const rejectRegex = document.getElementById("txtRejectRegex");
            if (rejectRegex) config.RejectTitleRegex = rejectRegex.value.trim();

            const chkAccept = document.getElementById("chkAcceptOverride");
            if (chkAccept) config.AcceptOverrideEnabled = chkAccept.checked;
            const txtAccept = document.getElementById("txtAcceptOverride");
            if (txtAccept) config.AcceptOverrideExpression = txtAccept.value;
            const chkReject = document.getElementById("chkRejectOverride");
            if (chkReject) config.RejectOverrideEnabled = chkReject.checked;
            const txtReject = document.getElementById("txtRejectOverride");
            if (txtReject) config.RejectOverrideExpression = txtReject.value;
        }

        // Output (only read from DOM when the Output tab is active)
        if (activeTabId === "output") {
            const chkDefault = document.getElementById("chkDefaultMarker");
            if (chkDefault) config.IncludeDefaultMarker = chkDefault.checked;
            const chkSdh = document.getElementById("chkExcludeSdh");
            if (chkSdh) config.ExcludeSdh = chkSdh.checked;
            const chkForced = document.getElementById("chkExcludeForced");
            if (chkForced) config.ExcludeForced = chkForced.checked;
            const chkOverwrite = document.getElementById("chkOverwrite");
            if (chkOverwrite) config.OverwriteExisting = chkOverwrite.checked;
            const chkConvertToSrt = document.getElementById("chkConvertToSrt");
            if (chkConvertToSrt) config.ConvertToSrt = chkConvertToSrt.checked;
        }

        return config;
    }

    /*** Init ***/
    async function init() {
        // Build tab nav
        const nav = document.getElementById("bseTabNav");
        nav.innerHTML = "";
        for (const tab of tabs) {
            const btn = document.createElement("button");
            btn.type = "button";
            btn.className = "bse-tab-button";
            btn.dataset.tabId = tab.id;
            btn.textContent = tab.label;
            btn.addEventListener("click", () => switchTab(tab.id));
            nav.appendChild(btn);
        }

        // Load config
        try {
            currentConfig = await window.ApiClient.getPluginConfiguration(PLUGIN_ID);
        } catch (e) {
            console.error("Better Subtitle Extractor: Failed to load config", e);
            currentConfig = {};
        }

        // Load libraries (needed by Libraries tab)
        await loadLibraries();

        // Render initial tab
        switchTab("general");
        takeSnapshot();
    }

    /*** Save ***/
    async function saveConfig() {
        const saveBtn = document.getElementById("saveConfig");
        const saveStatus = document.getElementById("saveStatus");
        const saveLabel = saveBtn?.querySelector("span");

        if (saveBtn) saveBtn.disabled = true;
        if (saveLabel) saveLabel.textContent = "Saving...";
        if (saveStatus) {
            saveStatus.textContent = "Saving...";
            saveStatus.dataset.state = "info";
            saveStatus.style.display = "inline";
        }

        try {
            const configToSave = gatherConfig();
            await window.ApiClient.updatePluginConfiguration(PLUGIN_ID, configToSave);
            currentConfig = configToSave;
            Dashboard.processPluginConfigurationUpdateResult({ Configuration: configToSave });

            if (saveStatus) {
                saveStatus.textContent = "Changes saved";
                saveStatus.dataset.state = "success";
                saveStatus.style.display = "inline";
                setTimeout(() => {
                    if (saveStatus.dataset.state === "success") {
                        saveStatus.style.display = "none";
                    }
                }, 3000);
            }

            takeSnapshot();
        } catch (e) {
            console.error("Better Subtitle Extractor: Failed to save config", e);
            if (saveStatus) {
                saveStatus.textContent = "Save failed";
                saveStatus.dataset.state = "error";
                saveStatus.style.display = "inline";
            }
            Dashboard.alert("Failed to save configuration.");
        } finally {
            if (saveBtn) saveBtn.disabled = false;
            if (saveLabel) saveLabel.textContent = "Save";
        }
    }

    /*** Event wiring ***/
    let changeHandler = null;
    let inputHandler = null;
    let beforeUnloadHandler = null;

    view.addEventListener("viewshow", async function () {
        // Wire up change listeners for dirty tracking
        changeHandler = (e) => {
            if (e.target.closest("#bseTabContent")) markDirty();
        };
        document.addEventListener("change", changeHandler);

        inputHandler = (e) => {
            if (e.target.closest("#bseTabContent")) markDirty();
        };
        document.addEventListener("input", inputHandler);

        // Warn before leaving with unsaved changes
        beforeUnloadHandler = (e) => {
            if (isDirty) {
                e.preventDefault();
                e.returnValue = "";
            }
        };
        window.addEventListener("beforeunload", beforeUnloadHandler);

        // Bind the save button once the view is attached
        view.querySelector("#saveConfig")?.addEventListener("click", async function (e) {
            e.preventDefault();
            await saveConfig();
        });

        await init();
    });

    view.addEventListener("viewdestroy", function () {
        if (changeHandler) {
            document.removeEventListener("change", changeHandler);
            changeHandler = null;
        }

        if (inputHandler) {
            document.removeEventListener("input", inputHandler);
            inputHandler = null;
        }

        if (beforeUnloadHandler) {
            window.removeEventListener("beforeunload", beforeUnloadHandler);
            beforeUnloadHandler = null;
        }
    });
}

// ES module export for import()
export default betterSubtitleExtractorController;