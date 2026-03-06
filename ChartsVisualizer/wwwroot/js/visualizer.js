window.visualizer = {
    diffEditor: null,

    createDiffEditor: function (originalCode, newCode, containerId) {
        require.config({ paths: { 'vs': 'https://cdn.jsdelivr.net/npm/monaco-editor@0.45.0/min/vs' } });
        require(['vs/editor/editor.main'], function () {
            const container = document.getElementById(containerId);
            if (!container) return;

            if (window.visualizer.diffEditor) {
                window.visualizer.diffEditor.dispose();
            }

            window.visualizer.diffEditor = monaco.editor.createDiffEditor(container, {
                originalEditable: false,
                readOnly: false,
                theme: 'vs-light'
            });

            const originalModel = monaco.editor.createModel(originalCode, 'javascript');
            const modifiedModel = monaco.editor.createModel(newCode, 'javascript');

            window.visualizer.diffEditor.setModel({
                original: originalModel,
                modified: modifiedModel
            });
        });
    },

    getDiffEditorValue: function () {
        if (window.visualizer.diffEditor) {
            return window.visualizer.diffEditor.getModifiedEditor().getValue();
        }
        return null;
    },

    disposeDiffEditor: function () {
        if (window.visualizer.diffEditor) {
            window.visualizer.diffEditor.dispose();
            window.visualizer.diffEditor = null;
        }
    },

    downloadFile: function (filename, content) {
        const element = document.createElement('a');
        element.setAttribute('href', 'data:text/html;charset=utf-8,' + encodeURIComponent(content));
        element.setAttribute('download', filename);

        element.style.display = 'none';
        document.body.appendChild(element);

        element.click();

        document.body.removeChild(element);
    },

    renderChart: function (code) {
        console.log("Received code to render:", code);

        // --- 1. Detect Tool Type ---
        let toolType = 'unknown';
        // Check standard prefixes from N8n
        if (code.match(/Tool:\s*Highcharts/i) || code.match(/Tool:\s*D3/i) || code.match(/Tool:\s*ECharts/i)) {
            toolType = 'container';
        } else if (code.match(/Tool:\s*Chart\.js/i)) {
            toolType = 'canvas';
        } else {
            // Heuristics if prefix missing
            if (code.includes('Highcharts') || code.includes('d3.select') || code.includes('echarts.init')) {
                toolType = 'container';
            } else if (code.includes('new Chart') || code.includes('getContext')) {
                toolType = 'canvas';
            }
        }

        const container = document.getElementById('container');
        const canvasContainer = document.getElementById('chartCanvasContainer');
        let canvas = document.getElementById('chartCanvas');

        // --- 2. Manage Visibility & Cleanup ---

        // Always cleanup generic container content to be safe
        // Also dispose ECharts instance if it exists on this container
        if (container) {
            // Check for ECharts instance
            if (typeof echarts !== 'undefined') {
                const existingInstance = echarts.getInstanceByDom(container);
                if (existingInstance) {
                    existingInstance.dispose();
                }
            }
            container.innerHTML = '';
        }

        // Always destroy Chart.js instance to be safe
        if (window.myChart instanceof Chart) {
            window.myChart.destroy();
            window.myChart = null;
        }

        if (toolType === 'container') {
            // Show Container, Hide Canvas Wrapper
            if (container) container.style.display = 'block';
            if (canvasContainer) canvasContainer.style.display = 'none';

        } else if (toolType === 'canvas') {
            // Hide Container, Show Canvas Wrapper
            if (container) container.style.display = 'none';
            if (canvasContainer) canvasContainer.style.display = 'block';

            // RECREATE Canvas to strictly reset dimensions and state
            // This prevents the "huge canvas" issue from persisting
            if (canvas && canvas.parentNode) {
                const newCanvas = document.createElement('canvas');
                newCanvas.id = 'chartCanvas';
                canvas.parentNode.replaceChild(newCanvas, canvas);
                canvas = newCanvas; // Update reference
            }
        } else {
            // Fallback: Ensure both are visible if we don't know?
            // Or default to container? Let's leave them visible but cleaned.
            if (container) container.style.display = 'block';
            if (canvasContainer) canvasContainer.style.display = 'block';
        }

        // --- 3. Prepare Code ---
        // Regex to extract code between markdown blocks
        const codeBlockRegex = /```(?:javascript|js)?\s*([\s\S]*?)\s*```/i;
        const match = code.match(codeBlockRegex);

        if (match && match[1]) {
            code = match[1];
        } else {
            // Fallback: cleanup prefixes if no regex match (legacy or raw code)
            if (code.trim().startsWith("Tool:") || code.trim().startsWith("Delegated to:")) {
                // Remove all lines until we find something that looks like code or variable declaration
                const lines = code.trim().split('\n');
                while (lines.length > 0 && (lines[0].trim().startsWith("Tool:") || lines[0].trim().startsWith("Delegated to:") || lines[0].trim() === '')) {
                    lines.shift();
                }
                code = lines.join('\n');
            }
        }

        try {
            // Execute the code
            eval(code);

            // --- 4. Post-Render Fixes ---
            // Fix D3 SVG scaling if necessary
            if (toolType === 'container') {
                const svg = container.querySelector('svg');
                if (svg) {
                    // If viewBox is missing, try to create one from width/height
                    if (!svg.hasAttribute('viewBox')) {
                        const w = parseFloat(svg.getAttribute('width')) || container.clientWidth;
                        const h = parseFloat(svg.getAttribute('height')) || container.clientHeight;
                        if (w && h) {
                            svg.setAttribute('viewBox', `0 0 ${w} ${h}`);
                        }
                    }
                    // Force responsive behavior so it fits in the 400px container
                    svg.style.width = '100%';
                    svg.style.height = '100%';
                    // We remove fixed attributes to let CSS take over (controlled by viewBox)
                    // But we keep them if they are percentages.
                    // Safest is often to rely on viewBox + CSS width/height: 100%
                }
            }

            return "Success";
        } catch (e) {
            console.error("Error executing chart code:", e);
            return "Error: " + e.message;
        }
    },

    // --- Statistical Helpers for AI ---
    utils: {
        sum: (data, key) => data.reduce((acc, row) => acc + (parseFloat(row[key]) || 0), 0),
        avg: (data, key) => data.length ? (window.visualizer.utils.sum(data, key) / data.length) : 0,
        min: (data, key) => data.length ? Math.min(...data.map(row => parseFloat(row[key]) || 0)) : 0,
        max: (data, key) => data.length ? Math.max(...data.map(row => parseFloat(row[key]) || 0)) : 0,
        formatCurrency: (val) => new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(val),
        groupBy: (data, key) => {
            return data.reduce((acc, row) => {
                const group = row[key];
                if (!acc[group]) acc[group] = [];
                acc[group].push(row);
                return acc;
            }, {});
        }
    }
};
