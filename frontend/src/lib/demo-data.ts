export interface DemoField {
  logicalName: string;
  displayName: string;
  fieldType: string;
}

export interface DemoTable {
  logicalName: string;
  displayName: string;
  fields: DemoField[];
}

export interface DemoComponent {
  name: string;
  type: string;
}

export interface DemoSecurityRole {
  name: string;
}

export interface DemoSolution {
  displayName: string;
  uniqueName: string;
  prefix: string;
  version: string;
  tables: DemoTable[];
  components: DemoComponent[];
  securityRoles: DemoSecurityRole[];
}

export interface DemoEnvironment {
  name: string;
  url: string;
  type: string;
}

export const demoSolution: DemoSolution = {
  displayName: "Contoso Feedback",
  uniqueName: "ContosoFeedback",
  prefix: "ctso",
  version: "1.0.0.0",
  tables: [
    {
      logicalName: "ctso_feedback",
      displayName: "Feedback",
      fields: [
        { logicalName: "ctso_feedbackid", displayName: "Feedback ID", fieldType: "Uniqueidentifier" },
        { logicalName: "ctso_title", displayName: "Title", fieldType: "Text" },
        { logicalName: "ctso_description", displayName: "Description", fieldType: "Multiline" },
        { logicalName: "ctso_rating", displayName: "Rating", fieldType: "WholeNumber" },
        { logicalName: "ctso_category", displayName: "Category", fieldType: "Choice" },
        { logicalName: "ctso_status", displayName: "Status", fieldType: "Choice" },
        { logicalName: "ctso_submittedby", displayName: "Submitted By", fieldType: "Lookup" },
        { logicalName: "ctso_createdon", displayName: "Created On", fieldType: "DateTime" },
      ],
    },
    {
      logicalName: "ctso_feedbackresponse",
      displayName: "Feedback Response",
      fields: [
        { logicalName: "ctso_feedbackresponseid", displayName: "Response ID", fieldType: "Uniqueidentifier" },
        { logicalName: "ctso_feedbackid", displayName: "Feedback", fieldType: "Lookup" },
        { logicalName: "ctso_responsebody", displayName: "Response Body", fieldType: "Multiline" },
      ],
    },
  ],
  components: [
    { name: "Feedback Main Form", type: "Form" },
    { name: "Feedback Quick View", type: "Form" },
    { name: "Active Feedback", type: "View" },
    { name: "My Feedback", type: "View" },
    { name: "Feedback Response Sub-Grid", type: "View" },
    { name: "Feedback Dashboard", type: "Dashboard" },
    { name: "Set Default Status", type: "Business Rule" },
    { name: "Notify on High Rating", type: "Flow" },
  ],
  securityRoles: [
    { name: "Feedback Submitter" },
    { name: "Feedback Administrator" },
  ],
};

export const demoEnvironment: DemoEnvironment = {
  name: "contoso-dev.crm4",
  url: "https://contoso-dev.crm4.dynamics.com",
  type: "Development",
};
