export interface StaticQuestion {
  id: string;
  text: string;
  category?: string;
}

export interface ModuleQuestions {
  philosophy: StaticQuestion[];
  research: StaticQuestion[];
}

export const staticQuestions: ModuleQuestions = {
  philosophy: [
    { id: 'c1', text: 'How would you distinguish courage from recklessness in a case where the outcome is not yet known?', category: 'virtue' },
    { id: 'c2', text: 'Is justice something a community establishes through its institutions, or something each person cultivates, and can one exist without the other?', category: 'virtue' },
    { id: 'c3', text: 'Can wisdom be taught, or must it be arrived at individually, and what does each answer imply about education?', category: 'virtue' },
    { id: 'c4', text: 'Which contributes more to a good life, doing or reflecting, and can the two be separated?', category: 'ethics' },
    { id: 'c5', text: 'When would you say you have good reason to trust what you perceive?', category: 'perception' },
    { id: 'c6', text: 'What distinguishes believing something from knowing it?', category: 'knowledge' },
    { id: 'c7', text: 'Can something be beautiful and harmful at the same time, and what would that suggest about beauty?', category: 'aesthetics' },
    { id: 'c8', text: 'What factors contribute to someone remaining the same person as they grow older?', category: 'identity' },
    { id: 'c9', text: 'Is virtue one quality or several, and what happens when they appear to conflict?', category: 'ethics' },
    { id: 'c10', text: 'What should a community provide for its members, and what should it not require of them in return?', category: 'community' },
    { id: 'c11', text: 'Is freedom the absence of obstacles or the capacity to choose well, and can a person be unfree without being constrained?', category: 'freedom' },
    { id: 'c12', text: 'Does disagreement about something mean the truth is unsettled, or only that someone is mistaken?', category: 'knowledge' },
    { id: 'c13', text: 'What role does change play in how we experience time?', category: 'time' },
    { id: 'c14', text: 'Where does the value of things come from?', category: 'value' },
    { id: 'c15', text: 'Can a friendship survive inequality, and does friendship require mutual benefit?', category: 'relationships' },
    { id: 'c16', text: 'What makes a question worth asking?', category: 'inquiry' },
    { id: 'c17', text: 'What makes a doubt worth keeping?', category: 'inquiry' },
    { id: 'c18', text: 'When is it better not to know?', category: 'inquiry' },
    { id: 'c19', text: 'What does it mean to understand something?', category: 'inquiry' },
    { id: 'c20', text: 'Can a question be answered too completely?', category: 'inquiry' },
    { id: 'c21', text: 'What is a question that should not be asked?', category: 'inquiry' },
    { id: 'c22', text: 'When does asking become a way of not answering?', category: 'inquiry' },
    { id: 'c23', text: 'What makes a problem worth solving?', category: 'inquiry' },
    { id: 'c24', text: 'What is the difference between being right and being certain?', category: 'inquiry' },
    { id: 'c25', text: 'Is a belief you cannot abandon still a belief?', category: 'inquiry' },
  ],
  research: [
    { id: 'r1', text: 'When observations do not fit the current theory, how do you decide where the problem lies?', category: 'theory' },
    { id: 'r2', text: 'When two frameworks explain the same evidence, what should decide between them?', category: 'comparison' },
    { id: 'r3', text: 'How can a researcher detect bias in their own work?', category: 'bias' },
    { id: 'r4', text: 'What should a thesis defense actually test?', category: 'evaluation' },
    { id: 'r5', text: 'What is lost when a finding is generalized beyond the group studied?', category: 'generalization' },
    { id: 'r6', text: 'How should the stated limits of a study shape the way its findings are used?', category: 'interpretation' },
    { id: 'r7', text: 'When two paradigms disagree over the same evidence, what allows the disagreement to stay productive?', category: 'pluralism' },
    { id: 'r8', text: 'What does combining two opposing views achieve, and what gets lost in the combination?', category: 'synthesis' },
    { id: 'r9', text: 'What would make a test fair to the theory being tested?', category: 'validation' },
    { id: 'r10', text: 'How should the benefits of research be weighed against costs borne by participants?', category: 'ethics' },
    { id: 'r11', text: 'What happens when a body of knowledge is used to examine itself?', category: 'reflexivity' },
    { id: 'r12', text: 'Can an observer avoid affecting what is observed?', category: 'measurement' },
    { id: 'r13', text: 'How does knowing you are being evaluated change the way you reason?', category: 'response' },
    { id: 'r14', text: 'Does understanding a problem require experiencing its consequences?', category: 'engagement' },
    { id: 'r15', text: 'When a result confirms something unexpected, how do you tell discovery from rediscovery?', category: 'novelty' },
  ],
};
