const OpenAI = require("openai");

const openai = new OpenAI({ apiKey: process.env.OPENAI_API_KEY });

// Example reuse of Velocity's AI pattern: turn a messy trade note into a behavioral tag + one-line insight.
async function classifyTradeNote(noteText) {
  const response = await openai.chat.completions.create({
    model: "gpt-4o-mini",
    temperature: 0,
    messages: [
      {
        role: "system",
        content:
          "You classify a futures trader's trade note into one tag " +
          "(a_plus | plan | revenge | fomo | boredom) and give one short, honest insight. " +
          "Return JSON: { tag, insight }.",
      },
      { role: "user", content: noteText },
    ],
  });
  return response.choices[0].message.content || "";
}

module.exports = { classifyTradeNote };
