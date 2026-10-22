/** @type {import('tailwindcss').Config} */
module.exports = {
    // Sirf wahi pages scan karte hain jahan Tailwind classes use hote hain.
    // Naya page Tailwind karna ho to bas yahan file ka path add karo.
    content: [
        "./Views/DailySupport/Detail.cshtml"
    ],

    // IMPORTANT: preflight off. Is project me poori styling
    // hand-written site.css me hai. Tailwind ka CSS reset
    // (preflight) on karne se baaki saare pages break ho jate.
    corePlugins: {
        preflight: false
    },

    theme: {
        extend: {}
    },

    plugins: []
};
